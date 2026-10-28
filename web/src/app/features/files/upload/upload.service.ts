import { Injectable, signal } from '@angular/core';
import { FilesApiService } from '../../../core/api/files-api.service';
import { AuthService } from '../../../core/auth/auth.service';
import { RuntimeConfigService } from '../../../core/config/runtime-config.service';
import type { FileMetadata, UploadSession } from '../../../core/models/file-metadata';

export type UploadItemStatus =
  | 'validating'
  | 'uploading'
  | 'processing'
  | 'available'
  | 'quarantined'
  | 'error';

export interface UploadItem {
  id: string;
  file: File;
  fileName: string;
  totalBytes: number;
  uploadedBytes: number;
  chunkCount: number;
  chunksDone: number;
  status: UploadItemStatus;
  scanStatus?: string;
  fileId?: string;
  error?: string;
}

const MAX_UPLOAD_BYTES = 5 * 1024 * 1024 * 1024; // 5 GiB (Storage__MaxFileSizeBytes default)
const CHUNKS_IN_FLIGHT = 2;
const CHUNK_ATTEMPTS = 3;
const POLL_INTERVAL_MS = 2000;

/**
 * Chunked upload orchestration (ADR-0003): validate → create session → PUT chunks with XHR
 * (progress, 2 in flight, per-chunk retry/backoff, X-Chunk-SHA256) → resume via status → complete
 * → poll the file detail until available/quarantined. The browser never talks to S3.
 */
@Injectable({ providedIn: 'root' })
export class UploadService {
  private readonly itemsSignal = signal<UploadItem[]>([]);

  readonly items = this.itemsSignal.asReadonly();

  constructor(
    private readonly api: FilesApiService,
    private readonly auth: AuthService,
    private readonly config: RuntimeConfigService,
  ) {}

  addFiles(files: File[]): void {
    for (const file of files) {
      const item = this.createItem(file);
      if (item.status === 'error') {
        this.itemsSignal.update((q) => [...q, item]);
        continue;
      }
      this.itemsSignal.update((q) => [...q, item]);
      void this.run(item);
    }
  }

  remove(id: string): void {
    this.itemsSignal.update((q) => q.filter((i) => i.id !== id));
  }

  private createItem(file: File): UploadItem {
    const item: UploadItem = {
      id: crypto.randomUUID(),
      file,
      fileName: file.name,
      totalBytes: file.size,
      uploadedBytes: 0,
      chunkCount: 0,
      chunksDone: 0,
      status: 'validating',
    };

    if (file.size > MAX_UPLOAD_BYTES) {
      item.status = 'error';
      item.error = $localize`:@@upload.tooLarge:El archivo supera el tamaño máximo permitido (5 GiB).`;
    }
    return item;
  }

  private async run(item: UploadItem): Promise<void> {
    try {
      const token = await this.auth.acquireToken();
      if (!token) {
        throw new Error($localize`:@@upload.notSignedIn:No se pudo autenticar la subida.`);
      }

      const session = await this.api.createSession({
        fileName: item.fileName,
        sizeBytes: item.file.size,
        contentType: item.file.type || 'application/octet-stream',
      });
      this.patch(item, {
        status: 'uploading',
        fileId: session.fileId,
        chunkCount: session.chunkCount,
        uploadedBytes: 0,
        chunksDone: 0,
      });

      const received = await this.resumeProgress(session);
      await this.uploadChunks(item, session, received, token);

      const completed = await this.api.complete(session.uploadSessionId);
      this.applyCompletion(item, completed);
      if (completed.status === 'processing') {
        await this.poll(item, completed.id);
      }
    } catch (error) {
      this.patch(item, { status: 'error', error: this.describeError(error) });
    }
  }

  /** Returns the set of chunk numbers already staged (resume), or empty for a fresh upload. */
  private async resumeProgress(session: UploadSession): Promise<Set<number>> {
    if (session.receivedBytes <= 0) {
      return new Set();
    }
    try {
      const status = await this.api.getStatus(session.uploadSessionId);
      return new Set(status.chunksReceived);
    } catch {
      return new Set();
    }
  }

  private async uploadChunks(
    item: UploadItem,
    session: UploadSession,
    received: Set<number>,
    token: string,
  ): Promise<void> {
    const pending: number[] = [];
    for (let n = 1; n <= session.chunkCount; n++) {
      if (!received.has(n)) {
        pending.push(n);
      }
    }

    let next = 0;
    const workers = Array.from({ length: Math.min(CHUNKS_IN_FLIGHT, pending.length || 1) }, async () => {
      while (true) {
        const index = next++;
        if (index >= pending.length) {
          return;
        }
        const chunkNumber = pending[index];
        const blob = this.chunk(item.file, chunkNumber, session.chunkSizeBytes);
        const checksum = await this.sha256Hex(blob);
        await this.uploadChunkWithRetry(session.uploadSessionId, chunkNumber, blob, checksum, token);
        this.patch(item, {
          uploadedBytes: Math.min(item.totalBytes, item.uploadedBytes + blob.size),
          chunksDone: item.chunksDone + 1,
        });
      }
    });

    await Promise.all(workers);
  }

  private async uploadChunkWithRetry(
    sessionId: string,
    chunkNumber: number,
    blob: Blob,
    checksum: string,
    token: string,
  ): Promise<void> {
    let lastError: unknown;
    for (let attempt = 0; attempt < CHUNK_ATTEMPTS; attempt++) {
      try {
        await this.putChunk(sessionId, chunkNumber, blob, checksum, token);
        return;
      } catch (error) {
        lastError = error;
        await delay(500 * 2 ** attempt);
      }
    }
    throw lastError;
  }

  private putChunk(
    sessionId: string,
    chunkNumber: number,
    blob: Blob,
    checksum: string,
    token: string,
  ): Promise<void> {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      const url = `${this.config.config().apiBaseUrl}/v1/portal/upload-sessions/${sessionId}/chunks/${chunkNumber}`;
      xhr.open('PUT', url);
      xhr.setRequestHeader('Authorization', `Bearer ${token}`);
      xhr.setRequestHeader('Content-Type', 'application/octet-stream');
      xhr.setRequestHeader('X-Chunk-SHA256', checksum);
      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          resolve();
        } else {
          reject(new Error(`chunk ${chunkNumber} failed (${xhr.status})`));
        }
      };
      xhr.onerror = () => reject(new Error(`chunk ${chunkNumber} network error`));
      xhr.ontimeout = () => reject(new Error(`chunk ${chunkNumber} timed out`));
      xhr.send(blob);
    });
  }

  private async poll(item: UploadItem, fileId: string): Promise<void> {
    for (;;) {
      await delay(POLL_INTERVAL_MS);
      const meta = await this.api.get(fileId);
      this.applyCompletion(item, meta);
      if (meta.status === 'available' || meta.status === 'quarantined') {
        return;
      }
    }
  }

  private applyCompletion(item: UploadItem, meta: FileMetadata): void {
    const status: UploadItemStatus =
      meta.status === 'available'
        ? 'available'
        : meta.status === 'quarantined'
          ? 'quarantined'
          : 'processing';
    this.patch(item, { status, scanStatus: meta.scanStatus });
  }

  private chunk(file: File, chunkNumber: number, chunkSizeBytes: number): Blob {
    const start = (chunkNumber - 1) * chunkSizeBytes;
    return file.slice(start, Math.min(start + chunkSizeBytes, file.size));
  }

  private async sha256Hex(blob: Blob): Promise<string> {
    const digest = await crypto.subtle.digest('SHA-256', await blob.arrayBuffer());
    return Array.from(new Uint8Array(digest))
      .map((b) => b.toString(16).padStart(2, '0'))
      .join('');
  }

  private patch(item: UploadItem, changes: Partial<UploadItem>): void {
    this.itemsSignal.update((q) => q.map((i) => (i.id === item.id ? { ...i, ...changes } : i)));
  }

  private describeError(error: unknown): string {
    if (error instanceof Error) {
      return error.message;
    }
    return $localize`:@@upload.failed:No se pudo completar la subida.`;
  }
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

import { TestBed } from '@angular/core/testing';
import { UploadService } from './upload.service';
import { FilesApiService } from '../../../core/api/files-api.service';
import { AuthService } from '../../../core/auth/auth.service';
import { RuntimeConfigService } from '../../../core/config/runtime-config.service';
import type { FileMetadata, UploadSession, UploadSessionStatus } from '../../../core/models/file-metadata';

type XhrCall = { method: string; url: string; headers: Record<string, string> };

let xhrCalls: XhrCall[] = [];
let failFirstN = 0;
let xhrStatus = 200;

class FakeXhr {
  status = 200;
  upload = {};
  onload: (() => void) | null = null;
  onerror: (() => void) | null = null;
  ontimeout: (() => void) | null = null;
  private method = '';
  private url = '';
  private headers: Record<string, string> = {};

  open(method: string, url: string): void {
    this.method = method;
    this.url = url;
  }

  setRequestHeader(name: string, value: string): void {
    this.headers[name] = value;
  }

  send(): void {
    xhrCalls.push({ method: this.method, url: this.url, headers: this.headers });
    if (failFirstN > 0) {
      failFirstN--;
      this.status = 500;
    } else {
      this.status = xhrStatus;
    }
    setTimeout(() => this.onload?.(), 0);
  }
}

function session(overrides: Partial<UploadSession> = {}): UploadSession {
  return {
    fileId: 'file-1',
    uploadSessionId: 'session-1',
    chunkSizeBytes: 16,
    chunkCount: 2,
    expiresAt: new Date().toISOString(),
    receivedBytes: 0,
    ...overrides,
  };
}

function metadata(status: FileMetadata['status']): FileMetadata {
  return {
    id: 'file-1',
    fileName: 'a.txt',
    sizeBytes: 32,
    contentType: 'text/plain',
    status,
    accessMode: 'owner_only',
    retention: { mode: 'finite', days: 30, expiresAt: null, reason: null },
    tags: [],
    downloads: { count: 0, lastAt: null },
    scanStatus: status === 'available' ? 'clean' : 'pending',
    legalHold: false,
    legalHoldReason: null,
    legalHoldReviewAt: null,
    owner: { type: 'human', displayName: null },
    quarantineReason: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    availableAt: null,
    deletedAt: null,
    version: 1,
    etag: '"1"',
  };
}

describe('UploadService', () => {
  let api: jasmine.SpyObj<FilesApiService>;
  let auth: jasmine.SpyObj<AuthService>;

  beforeEach(() => {
    xhrCalls = [];
    failFirstN = 0;
    xhrStatus = 200;
    (globalThis as unknown as { XMLHttpRequest: unknown }).XMLHttpRequest = FakeXhr;

    api = jasmine.createSpyObj<FilesApiService>('FilesApiService', [
      'createSession',
      'getStatus',
      'complete',
      'get',
      'abort',
    ]);
    auth = jasmine.createSpyObj<AuthService>('AuthService', ['acquireToken']);
    auth.acquireToken.and.resolveTo('token-1');

    TestBed.configureTestingModule({
      providers: [
        UploadService,
        { provide: FilesApiService, useValue: api },
        { provide: AuthService, useValue: auth },
        { provide: RuntimeConfigService, useValue: { config: () => ({ apiBaseUrl: 'http://api' }) } },
      ],
    });
  });

  it('creates a session, uploads every chunk, completes, and becomes available', async () => {
    api.createSession.and.resolveTo(session());
    api.getStatus.and.resolveTo({} as UploadSessionStatus);
    api.complete.and.resolveTo(metadata('available'));

    const service = TestBed.inject(UploadService);
    const file = new File([new Uint8Array(32)], 'a.txt', { type: 'text/plain' });
    service.addFiles([file]);

    await waitFor(() => api.complete.calls.count() === 1);

    expect(api.createSession).toHaveBeenCalledWith(
      jasmine.objectContaining({ fileName: 'a.txt', sizeBytes: 32 }),
    );
    expect(xhrCalls.map((c) => c.url)).toEqual([
      'http://api/v1/portal/upload-sessions/session-1/chunks/1',
      'http://api/v1/portal/upload-sessions/session-1/chunks/2',
    ]);
    expect(xhrCalls.every((c) => c.headers['X-Chunk-SHA256']?.match(/^[a-f0-9]{64}$/))).toBeTrue();
    expect(xhrCalls.every((c) => c.headers['Authorization'] === 'Bearer token-1')).toBeTrue();
    expect(api.complete).toHaveBeenCalledWith('session-1');
    expect(service.items()[0].status).toBe('available');
  });

  it('retries a failed chunk with backoff and succeeds', async () => {
    api.createSession.and.resolveTo(session({ chunkCount: 1 }));
    api.complete.and.resolveTo(metadata('available'));
    failFirstN = 2; // first two attempts fail, third succeeds

    const service = TestBed.inject(UploadService);
    service.addFiles([new File([new Uint8Array(16)], 'a.txt', { type: 'text/plain' })]);

    await waitFor(() => xhrCalls.filter((c) => c.url.includes('/chunks/1')).length === 3, 5000);

    expect(service.items()[0].status).toBe('available');
  });

  it('skips chunks already received on resume', async () => {
    api.createSession.and.resolveTo(session({ receivedBytes: 16, chunkCount: 2 }));
    api.getStatus.and.resolveTo({ chunksReceived: [1] } as UploadSessionStatus);
    api.complete.and.resolveTo(metadata('available'));

    const service = TestBed.inject(UploadService);
    service.addFiles([new File([new Uint8Array(32)], 'a.txt', { type: 'text/plain' })]);

    await waitFor(() => api.complete.calls.count() === 1);

    expect(xhrCalls.map((c) => c.url)).toEqual([
      'http://api/v1/portal/upload-sessions/session-1/chunks/2',
    ]);
  });

  it('rejects oversized files before any request', () => {
    const service = TestBed.inject(UploadService);
    const huge = new File([], 'huge.bin', { type: 'application/octet-stream' });
    Object.defineProperty(huge, 'size', { value: 6 * 1024 * 1024 * 1024 });

    service.addFiles([huge]);

    expect(service.items()[0].status).toBe('error');
    expect(api.createSession).not.toHaveBeenCalled();
  });
});

async function waitFor(condition: () => boolean, timeoutMs = 3000): Promise<void> {
  const start = Date.now();
  while (!condition()) {
    if (Date.now() - start > timeoutMs) {
      throw new Error('waitFor timed out');
    }
    await new Promise((resolve) => setTimeout(resolve, 10));
  }
}

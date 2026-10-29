import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FilesApiService } from '../../core/api/files-api.service';
import { problemMessage, toProblem } from '../../core/http/problem';
import type { LinkCreated } from '../../core/models/download-links';

/**
 * Creates a public link and shows the share URL + 8-character download key exactly once (ADR-0005).
 * The key is never echoed again; the owner revokes the link to rotate it.
 */
@Component({
  selector: 'app-link-create-dialog',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './link-create-dialog.html',
})
export class LinkCreateDialog {
  @Input({ required: true }) fileId!: string;
  @Output() created = new EventEmitter<LinkCreated>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly api = inject(FilesApiService);

  readonly creating = signal(false);
  readonly error = signal<string | null>(null);
  readonly result = signal<LinkCreated | null>(null);
  readonly copiedUrl = signal(false);
  readonly copiedKey = signal(false);

  maxDownloads = '';

  get fullUrl(): string {
    const link = this.result();
    return link ? `${window.location.origin}${link.url}` : '';
  }

  async create(): Promise<void> {
    this.error.set(null);

    let maxDownloads: number | undefined;
    const raw = this.maxDownloads.trim();
    if (raw) {
      const value = Number(raw);
      if (!Number.isInteger(value) || value < 1 || value > 1_000_000) {
        this.error.set($localize`:@@link.maxDownloadsInvalid:El máximo de descargas debe ser un entero entre 1 y 1000000.`);
        return;
      }
      maxDownloads = value;
    }

    this.creating.set(true);
    try {
      this.result.set(await this.api.createLink(this.fileId, maxDownloads));
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
    } finally {
      this.creating.set(false);
    }
  }

  async copyUrl(): Promise<void> {
    await this.copy(this.fullUrl);
    this.copiedUrl.set(true);
  }

  async copyKey(): Promise<void> {
    const key = this.result()?.downloadKey;
    if (key) {
      await this.copy(key);
      this.copiedKey.set(true);
    }
  }

  copyLabel(copied: boolean): string {
    return copied ? $localize`:@@link.copied:Copiado` : $localize`:@@link.copy:Copiar`;
  }

  private async copy(value: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      // Clipboard is unavailable (non-secure context); the value is still visible for manual copy.
    }
  }
}

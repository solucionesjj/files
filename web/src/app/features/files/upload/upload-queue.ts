import { Component, inject } from '@angular/core';
import { UploadService, type UploadItemStatus } from './upload.service';

const STATUS_LABELS: Record<UploadItemStatus, string> = {
  validating: $localize`:@@upload.validating:Preparando`,
  uploading: $localize`:@@upload.uploading:Subiendo`,
  processing: $localize`:@@upload.processing:Escaneando`,
  available: $localize`:@@upload.available:Disponible`,
  quarantined: $localize`:@@upload.quarantined:En cuarentena`,
  error: $localize`:@@upload.error:Error`,
};

/** Renders in-flight and recently finished uploads with per-file progress. */
@Component({
  selector: 'app-upload-queue',
  standalone: true,
  template: `
    <div class="upload-queue d-grid gap-2" aria-live="polite">
      @for (item of upload.items(); track item.id) {
        <div class="card">
          <div class="card-body py-2">
            <div class="d-flex justify-content-between align-items-center">
              <span class="text-truncate me-2" title="{{ item.fileName }}">{{ item.fileName }}</span>
              <div class="d-flex align-items-center gap-2">
                <span class="badge text-bg-light">{{ label(item.status) }}</span>
                <button
                  type="button"
                  class="btn-close"
                  aria-label="Quitar"
                  i18n-aria-label="@@upload.remove"
                  (click)="upload.remove(item.id)"
                ></button>
              </div>
            </div>
            <div class="progress mt-1" style="height: 6px">
              <div
                class="progress-bar"
                role="progressbar"
                [style.width.%]="percent(item)"
                [attr.aria-valuenow]="percent(item)"
                aria-valuemin="0"
                aria-valuemax="100"
              ></div>
            </div>
            @if (item.error) {
              <div class="text-danger small mt-1">{{ item.error }}</div>
            }
          </div>
        </div>
      }
    </div>
  `,
})
export class UploadQueue {
  protected readonly upload = inject(UploadService);

  protected percent(item: { uploadedBytes: number; totalBytes: number }): number {
    if (item.totalBytes <= 0) {
      return 100;
    }
    return Math.min(100, Math.round((item.uploadedBytes / item.totalBytes) * 100));
  }

  protected label(status: UploadItemStatus): string {
    return STATUS_LABELS[status];
  }
}

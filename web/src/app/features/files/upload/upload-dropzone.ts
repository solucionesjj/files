import { Component, ElementRef, inject } from '@angular/core';
import { UploadService } from './upload.service';

/** Drag-and-drop + file-picker entry point that hands selected files to the upload orchestrator. */
@Component({
  selector: 'app-upload-dropzone',
  standalone: true,
  template: `
    <div
      class="upload-dropzone border rounded-3 text-center p-4"
      [class.dragging]="dragging"
      (dragover)="onDragOver($event)"
      (dragleave)="onDragLeave($event)"
      (drop)="onDrop($event)"
    >
      <input
        #picker
        type="file"
        class="d-none"
        multiple
        (change)="onPicked(picker)"
      />
      <div class="fs-1">⬆️</div>
      <p class="mb-1 fw-semibold" i18n="@@upload.dropHint">Arrastra archivos aquí o</p>
      <button type="button" class="btn btn-primary btn-sm" (click)="openPicker()" i18n="@@upload.pick">
        selecciona archivos
      </button>
      <p class="text-muted small mt-2 mb-0" i18n="@@upload.dropNote">
        La subida se divide en fragmentos y se reanuda si se interrumpe.
      </p>
    </div>
  `,
  styles: [
    `
      .upload-dropzone {
        border-style: dashed;
        cursor: pointer;
      }
      .upload-dropzone.dragging {
        background-color: var(--bs-primary-bg-subtle);
        border-color: var(--bs-primary);
      }
    `,
  ],
})
export class UploadDropzone {
  private readonly upload = inject(UploadService);
  private readonly host = inject(ElementRef);

  protected dragging = false;

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging = true;
  }

  onDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.dragging = false;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging = false;
    const files = event.dataTransfer?.files;
    if (files?.length) {
      this.upload.addFiles(Array.from(files));
    }
  }

  onPicked(input: HTMLInputElement): void {
    if (input.files?.length) {
      this.upload.addFiles(Array.from(input.files));
    }
    input.value = '';
  }

  openPicker(): void {
    (this.host.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input[type=file]')?.click();
  }
}

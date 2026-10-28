import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FilesApiService } from '../../core/api/files-api.service';
import { problemMessage, toProblem } from '../../core/http/problem';
import type { FileMetadata } from '../../core/models/file-metadata';

/**
 * Delete confirmation modal. When the file has downloads, deletion requires typing the file name
 * (type-to-confirm). Handles 409 legal_hold_active inline.
 */
@Component({
  selector: 'app-file-delete-dialog',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './file-delete-dialog.html',
})
export class FileDeleteDialog {
  @Input({ required: true }) file!: FileMetadata;
  @Output() deleted = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly api = inject(FilesApiService);

  readonly deleting = signal(false);
  readonly error = signal<string | null>(null);

  confirmName = '';

  get requiresTyping(): boolean {
    return this.file.downloads.count > 0;
  }

  get canConfirm(): boolean {
    return !this.requiresTyping || this.confirmName.trim() === this.file.fileName;
  }

  async confirm(): Promise<void> {
    this.error.set(null);
    if (this.requiresTyping && this.confirmName.trim() !== this.file.fileName) {
      this.error.set($localize`:@@delete.nameMismatch:El nombre escrito no coincide.`);
      return;
    }

    this.deleting.set(true);
    try {
      await this.api.delete(this.file.id);
      this.deleted.emit();
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
    } finally {
      this.deleting.set(false);
    }
  }
}

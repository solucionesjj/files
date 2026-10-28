import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FilesApiService } from '../../core/api/files-api.service';
import { problemMessage, toProblem } from '../../core/http/problem';
import type { FileMetadata } from '../../core/models/file-metadata';

const DEFAULT_DAYS = 30;
const MAX_DAYS = 365;
const MIN_NOTE = 10;

/**
 * Modal for rename / retag / retention. PATCHes with `If-Match` (the file's ETag) and maps the
 * 412 stale_version, 409 legal_hold_active, and 422 retention problems to an inline message.
 */
@Component({
  selector: 'app-file-edit-dialog',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './file-edit-dialog.html',
})
export class FileEditDialog implements OnInit {
  @Input({ required: true }) file!: FileMetadata;
  @Output() saved = new EventEmitter<FileMetadata>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly api = inject(FilesApiService);

  readonly saving = signal(false);
  readonly error = signal<string | null>(null);

  fileName = '';
  tags = '';
  days = DEFAULT_DAYS;
  note = '';

  ngOnInit(): void {
    this.fileName = this.file.fileName;
    this.tags = (this.file.tags ?? []).join(', ');
    this.days = this.file.retention.days ?? DEFAULT_DAYS;
    this.note = this.file.retention.reason ?? '';
  }

  get noteRequired(): boolean {
    return this.days > DEFAULT_DAYS;
  }

  async save(): Promise<void> {
    this.error.set(null);

    const fileName = this.fileName.trim();
    if (!fileName) {
      this.error.set($localize`:@@edit.nameRequired:El nombre es obligatorio.`);
      return;
    }

    if (this.days < 1 || this.days > MAX_DAYS) {
      this.error.set($localize`:@@edit.daysRange:La retención debe estar entre 1 y 365 días.`);
      return;
    }

    const note = this.note.trim();
    if (this.noteRequired && note.length < MIN_NOTE) {
      this.error.set($localize`:@@edit.noteRequired:Se requiere un motivo de al menos 10 caracteres.`);
      return;
    }

    const tags = this.tags
      .split(',')
      .map((t) => t.trim())
      .filter((t) => t.length > 0);

    this.saving.set(true);
    try {
      const updated = await this.api.update(
        this.file.id,
        {
          fileName,
          retention: { mode: 'finite', days: this.days, reason: note || undefined },
          tags,
        },
        this.file.etag,
      );
      this.saved.emit(updated);
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
    } finally {
      this.saving.set(false);
    }
  }
}

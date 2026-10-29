import { Component, EventEmitter, Input, Output, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FilesApiService } from '../../core/api/files-api.service';
import { problemMessage, toProblem } from '../../core/http/problem';
import type { Link } from '../../core/models/download-links';

/** Revokes a public link (optionally with a reason); outstanding grants become invalid immediately. */
@Component({
  selector: 'app-link-revoke-dialog',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './link-revoke-dialog.html',
})
export class LinkRevokeDialog {
  @Input({ required: true }) link!: Link;
  @Output() revoked = new EventEmitter<void>();
  @Output() cancelled = new EventEmitter<void>();

  private readonly api = inject(FilesApiService);

  readonly revoking = signal(false);
  readonly error = signal<string | null>(null);
  reason = '';

  async revoke(): Promise<void> {
    this.error.set(null);
    this.revoking.set(true);
    try {
      await this.api.revokeLink(this.link.id, this.reason.trim() || undefined);
      this.revoked.emit();
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
    } finally {
      this.revoking.set(false);
    }
  }
}

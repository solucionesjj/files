import { Component, EventEmitter, Input, OnInit, Output, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FilesApiService } from '../../core/api/files-api.service';
import { problemMessage, toProblem } from '../../core/http/problem';
import type { Link, LinkStats } from '../../core/models/download-links';

/** Shows per-link download statistics (successful/reserved/failed + recent events). */
@Component({
  selector: 'app-link-stats-dialog',
  standalone: true,
  imports: [DatePipe],
  templateUrl: './link-stats-dialog.html',
})
export class LinkStatsDialog implements OnInit {
  @Input({ required: true }) link!: Link;
  @Output() closed = new EventEmitter<void>();

  private readonly api = inject(FilesApiService);

  readonly stats = signal<LinkStats | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.loading.set(true);
    this.error.set(null);
    try {
      this.stats.set(await this.api.getLinkStats(this.link.id));
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
    } finally {
      this.loading.set(false);
    }
  }
}

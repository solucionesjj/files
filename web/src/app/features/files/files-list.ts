import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { Subject, debounceTime, takeUntil } from 'rxjs';
import { FilesApiService } from '../../core/api/files-api.service';
import { DownloadService } from '../../core/api/download.service';
import { ToastService } from '../../core/http/toast.service';
import { toProblem } from '../../core/http/problem';
import type { FileMetadata, FileStatus } from '../../core/models/file-metadata';
import { FileStatusBadgePipe, FileStatusLabelPipe } from './file-status.pipe';
import { FileEditDialog } from './file-edit-dialog';
import { FileDeleteDialog } from './file-delete-dialog';
import { UploadDropzone } from './upload/upload-dropzone';
import { UploadQueue } from './upload/upload-queue';
import { UploadService } from './upload/upload.service';

const PAGE_SIZE = 25;

@Component({
  selector: 'app-files-list',
  standalone: true,
  imports: [
    RouterLink,
    FormsModule,
    DatePipe,
    FileStatusLabelPipe,
    FileStatusBadgePipe,
    FileEditDialog,
    FileDeleteDialog,
    UploadDropzone,
    UploadQueue,
  ],
  templateUrl: './files-list.html',
})
export class FilesList implements OnInit, OnDestroy {
  private readonly api = inject(FilesApiService);
  private readonly downloads = inject(DownloadService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  protected readonly upload = inject(UploadService);

  readonly items = signal<FileMetadata[]>([]);
  readonly loading = signal(false);
  readonly hasMore = signal(false);

  readonly q = signal('');
  readonly statuses = signal<FileStatus[]>([]);
  readonly sort = signal('-created_at');

  readonly editingFile = signal<FileMetadata | null>(null);
  readonly deletingFile = signal<FileMetadata | null>(null);

  readonly allStatuses: FileStatus[] = [
    'available',
    'processing',
    'quarantined',
    'expired',
    'pending_deletion',
    'deleted',
  ];

  readonly sortOptions = [
    { value: '-created_at', label: $localize`:@@list.sortNewest:Más recientes` },
    { value: 'created_at', label: $localize`:@@list.sortOldest:Más antiguos` },
    { value: 'name', label: $localize`:@@list.sortName:Nombre (A–Z)` },
    { value: '-name', label: $localize`:@@list.sortNameDesc:Nombre (Z–A)` },
    { value: '-downloads', label: $localize`:@@list.sortDownloads:Más descargados` },
  ];

  private nextCursor = signal<string | null>(null);
  private readonly search$ = new Subject<string>();
  private readonly destroy$ = new Subject<void>();

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntil(this.destroy$)).subscribe((params) => {
      const cursor = params.get('cursor');
      void this.load(cursor);
    });

    this.search$.pipe(debounceTime(300), takeUntil(this.destroy$)).subscribe(() => this.resetAndLoad());
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  onSearch(value: string): void {
    this.q.set(value);
    this.search$.next(value);
  }

  toggleStatus(status: FileStatus): void {
    this.statuses.update((current) =>
      current.includes(status) ? current.filter((s) => s !== status) : [...current, status],
    );
    this.resetAndLoad();
  }

  changeSort(value: string): void {
    this.sort.set(value);
    this.resetAndLoad();
  }

  nextPage(): void {
    const cursor = this.nextCursor();
    if (cursor) {
      void this.router.navigate([], { queryParams: { cursor } });
    }
  }

  download(file: FileMetadata): void {
    void this.downloads
      .download(file.id)
      .catch(async (error) => this.toast.errorFromProblem(await toProblem(error)));
  }

  isDownloadable(file: FileMetadata): boolean {
    return file.status === 'available';
  }

  openEdit(file: FileMetadata): void {
    this.editingFile.set(file);
  }

  openDelete(file: FileMetadata): void {
    this.deletingFile.set(file);
  }

  onSaved(updated: FileMetadata): void {
    this.items.update((current) => current.map((f) => (f.id === updated.id ? updated : f)));
    this.editingFile.set(null);
    this.toast.success($localize`:@@edit.saved:Archivo actualizado.`);
  }

  onDeleted(file: FileMetadata): void {
    this.items.update((current) => current.filter((f) => f.id !== file.id));
    this.deletingFile.set(null);
    this.toast.success($localize`:@@delete.deleted:Archivo eliminado.`);
  }

  sizeLabel(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    const units = ['KB', 'MB', 'GB', 'TB'];
    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
      value /= 1024;
      unit++;
    }
    return `${new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(value)} ${units[unit]}`;
  }

  expiryLabel(file: FileMetadata): string {
    if (!file.retention.expiresAt) {
      return $localize`:@@list.indefinite:Indefinido`;
    }
    return new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short', year: 'numeric' }).format(
      new Date(file.retention.expiresAt),
    );
  }

  private resetAndLoad(): void {
    void this.router.navigate([], { queryParams: {} });
    // Navigating to the current URL is a no-op (no queryParamMap emission), so reload directly.
    void this.load(null);
  }

  private async load(cursor: string | null): Promise<void> {
    this.loading.set(true);
    try {
      const result = await this.api.list({
        q: this.q().trim() || undefined,
        sort: this.sort(),
        status: this.statuses().length ? [...this.statuses()] : undefined,
        cursor: cursor ?? undefined,
        limit: PAGE_SIZE,
      });
      this.items.set(result.items);
      this.hasMore.set(result.hasMore);
      this.nextCursor.set(result.nextCursor);
    } catch (error) {
      this.toast.errorFromProblem(await toProblem(error));
    } finally {
      this.loading.set(false);
    }
  }
}

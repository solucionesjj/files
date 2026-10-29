import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { FilesApiService } from '../../core/api/files-api.service';
import { DownloadService } from '../../core/api/download.service';
import { ToastService } from '../../core/http/toast.service';
import { toProblem } from '../../core/http/problem';
import type { FileMetadata, ScanStatus } from '../../core/models/file-metadata';
import type { Link } from '../../core/models/download-links';
import { FileStatusBadgePipe, FileStatusLabelPipe } from './file-status.pipe';
import { FileEditDialog } from './file-edit-dialog';
import { FileDeleteDialog } from './file-delete-dialog';
import { LinkCreateDialog } from './link-create-dialog';
import { LinkRevokeDialog } from './link-revoke-dialog';
import { LinkStatsDialog } from './link-stats-dialog';

const SCAN_LABELS: Record<ScanStatus, string> = {
  not_scanned: $localize`:@@scan.notScanned:Sin escanear`,
  pending: $localize`:@@scan.pending:Pendiente`,
  clean: $localize`:@@scan.clean:Limpio`,
  infected: $localize`:@@scan.infected:Infectado`,
  failed: $localize`:@@scan.failed:Error de escaneo`,
};

@Component({
  selector: 'app-files-detail',
  standalone: true,
  imports: [
    RouterLink,
    DatePipe,
    FileStatusLabelPipe,
    FileStatusBadgePipe,
    FileEditDialog,
    FileDeleteDialog,
    LinkCreateDialog,
    LinkRevokeDialog,
    LinkStatsDialog,
  ],
  templateUrl: './files-detail.html',
})
export class FilesDetail implements OnInit {
  private readonly api = inject(FilesApiService);
  private readonly downloads = inject(DownloadService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);

  readonly file = signal<FileMetadata | null>(null);
  readonly loading = signal(false);
  readonly editing = signal(false);
  readonly deleting = signal(false);

  readonly links = signal<Link[]>([]);
  readonly linksLoading = signal(false);
  readonly creatingLink = signal(false);
  readonly revokingLink = signal<Link | null>(null);
  readonly statsLink = signal<Link | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      return;
    }
    this.loading.set(true);
    try {
      this.file.set(await this.api.get(id));
    } catch (error) {
      this.toast.errorFromProblem(await toProblem(error));
    } finally {
      this.loading.set(false);
    }
    void this.loadLinks(id);
  }

  async loadLinks(fileId: string): Promise<void> {
    this.linksLoading.set(true);
    try {
      this.links.set((await this.api.listFileLinks(fileId)).items);
    } catch (error) {
      this.toast.errorFromProblem(await toProblem(error));
    } finally {
      this.linksLoading.set(false);
    }
  }

  download(): void {
    const file = this.file();
    if (!file) {
      return;
    }
    void this.downloads
      .download(file.id)
      .catch(async (error) => this.toast.errorFromProblem(await toProblem(error)));
  }

  isDownloadable(file: FileMetadata): boolean {
    return file.status === 'available';
  }

  onLinkCreated(): void {
    this.creatingLink.set(false);
    const file = this.file();
    if (file) {
      void this.loadLinks(file.id);
    }
    this.toast.success($localize`:@@link.created:Enlace creado.`);
  }

  onLinkRevoked(): void {
    this.revokingLink.set(null);
    const file = this.file();
    if (file) {
      void this.loadLinks(file.id);
    }
    this.toast.success($localize`:@@link.revoked:Enlace revocado.`);
  }

  onSaved(updated: FileMetadata): void {
    this.file.set(updated);
    this.editing.set(false);
    this.toast.success($localize`:@@edit.saved:Archivo actualizado.`);
  }

  onDeleted(): void {
    this.toast.success($localize`:@@delete.deleted:Archivo eliminado.`);
    void this.router.navigate(['/files']);
  }

  scanLabel(scanStatus: ScanStatus): string {
    return SCAN_LABELS[scanStatus];
  }

  linkStatusLabel(status: Link['status']): string {
    switch (status) {
      case 'active':
        return $localize`:@@link.statusActive:Activo`;
      case 'revoked':
        return $localize`:@@link.statusRevoked:Revocado`;
      case 'expired':
        return $localize`:@@link.statusExpired:Vencido`;
      default:
        return status;
    }
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
}

import { Pipe, PipeTransform } from '@angular/core';
import type { FileStatus } from '../../core/models/file-metadata';

/** Localized, user-facing label for a file lifecycle status. */
@Pipe({ name: 'fileStatusLabel', standalone: true })
export class FileStatusLabelPipe implements PipeTransform {
  transform(status: FileStatus): string {
    switch (status) {
      case 'available':
        return $localize`:@@status.available:Disponible`;
      case 'processing':
        return $localize`:@@status.processing:Procesando`;
      case 'quarantined':
        return $localize`:@@status.quarantined:En cuarentena`;
      case 'expired':
        return $localize`:@@status.expired:Expirado`;
      case 'pending_deletion':
        return $localize`:@@status.pendingDeletion:Eliminación pendiente`;
      case 'deleted':
        return $localize`:@@status.deleted:Eliminado`;
      case 'pending_upload':
        return $localize`:@@status.pendingUpload:Subiendo`;
      case 'upload_failed':
        return $localize`:@@status.uploadFailed:Error de subida`;
      default:
        return status;
    }
  }
}

/** Bootstrap badge color for a file lifecycle status. */
@Pipe({ name: 'fileStatusBadge', standalone: true })
export class FileStatusBadgePipe implements PipeTransform {
  transform(status: FileStatus): string {
    switch (status) {
      case 'available':
        return 'text-bg-success';
      case 'processing':
      case 'pending_deletion':
        return 'text-bg-warning';
      case 'quarantined':
      case 'upload_failed':
        return 'text-bg-danger';
      case 'expired':
        return 'text-bg-secondary';
      case 'deleted':
        return 'text-bg-dark';
      case 'pending_upload':
        return 'text-bg-info';
      default:
        return 'text-bg-secondary';
    }
  }
}

import { Injectable, inject } from '@angular/core';
import { FilesApiService } from './files-api.service';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * Two-step owner download (ADR-0003/0005): request a 120 s single-use grant, then navigate the
 * browser to the capability URL. The API streams the bytes with `Content-Disposition: attachment`,
 * so no fetch-to-Blob is used (a 5 GiB file is never buffered in the browser).
 */
@Injectable({ providedIn: 'root' })
export class DownloadService {
  private readonly api = inject(FilesApiService);
  private readonly config = inject(RuntimeConfigService);

  async download(fileId: string): Promise<void> {
    const grant = await this.api.createDownloadGrant(fileId);
    this.navigate(`${this.config.config().apiBaseUrl}${grant.downloadUrl}`);
  }

  /** Navigates the current window to the capability URL (isolated for testability). */
  protected navigate(url: string): void {
    window.location.assign(url);
  }
}

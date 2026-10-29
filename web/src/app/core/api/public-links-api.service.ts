import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { DownloadGrant, LinkPublicInfo } from '../models/download-links';
import { RuntimeConfigService } from '../config/runtime-config.service';

/**
 * Anonymous public-link endpoints (getPublicLink / redeemPublicLink). These requests bypass the
 * auth interceptor (via `HttpBackend`) so a signed-out visitor on `/d/{token}` is never redirected
 * to Entra and never has a bearer token attached.
 */
@Injectable({ providedIn: 'root' })
export class PublicLinksApiService {
  private readonly http: HttpClient;

  constructor(
    backend: HttpBackend,
    private readonly config: RuntimeConfigService,
  ) {
    this.http = new HttpClient(backend);
  }

  getLink(token: string): Promise<LinkPublicInfo> {
    return firstValueFrom(this.http.get<LinkPublicInfo>(this.url(`/v1/public/download-links/${token}`)));
  }

  redeem(token: string, key: string): Promise<DownloadGrant> {
    return firstValueFrom(
      this.http.post<DownloadGrant>(this.url(`/v1/public/download-links/${token}/downloads`), { key }),
    );
  }

  private url(path: string): string {
    return `${this.config.config().apiBaseUrl}${path}`;
  }
}

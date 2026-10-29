import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { PublicLinksApiService } from '../../core/api/public-links-api.service';
import { RuntimeConfigService } from '../../core/config/runtime-config.service';
import { problemMessage, toProblem } from '../../core/http/problem';
import type { LinkPublicInfo } from '../../core/models/download-links';

/**
 * Anonymous landing page for a public link (`/d/{token}`). Fetches non-sensitive metadata, accepts
 * the 8-character key, redeems a 120 s single-use grant, and navigates the browser to the grant's
 * capability URL (two-step, never fetch-to-Blob). Invalid link/key/limit/lockout surface uniformly.
 */
@Component({
  selector: 'app-public-link',
  standalone: true,
  imports: [FormsModule, DatePipe],
  templateUrl: './public-link.html',
})
export class PublicLink implements OnInit {
  private readonly api = inject(PublicLinksApiService);
  private readonly config = inject(RuntimeConfigService);
  private readonly route = inject(ActivatedRoute);

  readonly info = signal<LinkPublicInfo | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly key = signal('');
  readonly submitting = signal(false);

  private get token(): string | null {
    return this.route.snapshot.paramMap.get('token');
  }

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    const token = this.token;
    if (!token) {
      this.error.set($localize`:@@publicLink.invalid:Enlace no válido.`);
      return;
    }

    this.loading.set(true);
    try {
      this.info.set(await this.api.getLink(token));
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
    } finally {
      this.loading.set(false);
    }
  }

  async redeem(): Promise<void> {
    const token = this.token;
    if (!token) {
      return;
    }

    const key = this.key().trim();
    if (!/^[A-Za-z0-9]{8}$/.test(key)) {
      this.error.set($localize`:@@publicLink.keyShape:La clave debe tener exactamente 8 caracteres alfanuméricos.`);
      return;
    }

    this.error.set(null);
    this.submitting.set(true);
    try {
      const grant = await this.api.redeem(token, key);
      this.navigate(`${this.config.config().apiBaseUrl}${grant.downloadUrl}`);
    } catch (err) {
      this.error.set(problemMessage(await toProblem(err)));
      this.submitting.set(false);
    }
  }

  /** Navigates the current window to the capability URL (isolated for testability). */
  protected navigate(url: string): void {
    window.location.assign(url);
  }

  sizeLabel(bytes: number | null): string {
    if (bytes === null) {
      return '';
    }
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

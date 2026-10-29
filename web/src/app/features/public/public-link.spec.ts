import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { PublicLink } from './public-link';
import { PublicLinksApiService } from '../../core/api/public-links-api.service';
import { RuntimeConfigService } from '../../core/config/runtime-config.service';
import type { DownloadGrant, LinkPublicInfo } from '../../core/models/download-links';

describe('PublicLink', () => {
  let api: jasmine.SpyObj<PublicLinksApiService>;

  const info: LinkPublicInfo = {
    fileName: 'report.pdf',
    sizeBytes: 1024,
    contentType: 'application/pdf',
    requiresKey: true,
    requiresAuthentication: false,
    expiresAt: new Date(Date.now() + 3600_000).toISOString(),
    remainingDownloads: 5,
    ownerDisplayName: 'Owner',
  };

  beforeEach(() => {
    api = jasmine.createSpyObj<PublicLinksApiService>('PublicLinksApiService', ['getLink', 'redeem']);
    TestBed.configureTestingModule({
      imports: [PublicLink, FormsModule],
      providers: [
        { provide: PublicLinksApiService, useValue: api },
        { provide: RuntimeConfigService, useValue: { config: () => ({ apiBaseUrl: 'http://api' }) } },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: new Map([['token', 'tok']]) } } },
      ],
    });
  });

  function create(): PublicLink {
    const fixture = TestBed.createComponent(PublicLink);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('loads metadata and redeems a valid key to the grant URL', async () => {
    api.getLink.and.resolveTo(info);
    const grant: DownloadGrant = {
      grantToken: 'grant',
      expiresAt: new Date().toISOString(),
      downloadUrl: '/v1/download-grants/grant',
    };
    api.redeem.and.resolveTo(grant);

    const page = create();
    await page.load();
    expect(page.info()?.fileName).toBe('report.pdf');

    const navigate = spyOn(page as unknown as { navigate: (url: string) => void }, 'navigate');
    page.key.set('Ab3xY9Qz');
    await page.redeem();

    expect(api.redeem).toHaveBeenCalledWith('tok', 'Ab3xY9Qz');
    expect(navigate).toHaveBeenCalledWith('http://api/v1/download-grants/grant');
  });

  it('rejects a malformed key before redeeming', async () => {
    api.getLink.and.resolveTo(info);
    const page = create();
    await page.load();

    page.key.set('short');
    await page.redeem();

    expect(api.redeem).not.toHaveBeenCalled();
    expect(page.error()).toBeTruthy();
  });

  it('maps a 404 link_not_found to a uniform message', async () => {
    api.getLink.and.rejectWith({ error: { code: 'link_not_found' } });
    const page = create();
    await page.load();

    expect(page.error()).toContain('no es válido');
  });
});

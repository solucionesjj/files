import { TestBed } from '@angular/core/testing';
import { DownloadService } from './download.service';
import { FilesApiService } from './files-api.service';
import { RuntimeConfigService } from '../config/runtime-config.service';
import type { DownloadGrant } from '../models/file-metadata';

describe('DownloadService', () => {
  let api: jasmine.SpyObj<FilesApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<FilesApiService>('FilesApiService', ['createDownloadGrant']);
    TestBed.configureTestingModule({
      providers: [
        DownloadService,
        { provide: FilesApiService, useValue: api },
        { provide: RuntimeConfigService, useValue: { config: () => ({ apiBaseUrl: 'http://api' }) } },
      ],
    });
  });

  it('requests a grant and navigates to the capability URL', async () => {
    const grant: DownloadGrant = {
      grantToken: 'tok_123',
      expiresAt: new Date().toISOString(),
      downloadUrl: '/v1/download-grants/tok_123',
    };
    api.createDownloadGrant.and.resolveTo(grant);

    const service = TestBed.inject(DownloadService);
    const navigate = spyOn(service as unknown as { navigate: (url: string) => void }, 'navigate');

    await service.download('file-1');

    expect(api.createDownloadGrant).toHaveBeenCalledWith('file-1');
    expect(navigate).toHaveBeenCalledWith('http://api/v1/download-grants/tok_123');
  });

  it('propagates grant issuance errors to the caller', async () => {
    api.createDownloadGrant.and.rejectWith({ error: { code: 'file_unavailable' } });

    const service = TestBed.inject(DownloadService);
    const navigate = spyOn(service as unknown as { navigate: (url: string) => void }, 'navigate');

    await expectAsync(service.download('file-1')).toBeRejected();
    expect(navigate).not.toHaveBeenCalled();
  });
});

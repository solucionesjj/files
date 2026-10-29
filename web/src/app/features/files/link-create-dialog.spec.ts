import { TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { LinkCreateDialog } from './link-create-dialog';
import { FilesApiService } from '../../core/api/files-api.service';
import type { LinkCreated } from '../../core/models/download-links';

function createdLink(overrides: Partial<LinkCreated> = {}): LinkCreated {
  return {
    id: 'link-1',
    fileId: 'file-1',
    accessMode: 'public',
    status: 'active',
    createdAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 15 * 24 * 3600_000).toISOString(),
    maxDownloads: null,
    successfulDownloads: 0,
    reservedDownloads: 0,
    revokedAt: null,
    createdBy: { type: 'user', displayName: null },
    url: '/d/tok',
    token: 'tok',
    downloadKey: 'Ab3xY9Qz',
    ...overrides,
  };
}

describe('LinkCreateDialog', () => {
  let api: jasmine.SpyObj<FilesApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<FilesApiService>('FilesApiService', ['createLink']);
    TestBed.configureTestingModule({
      imports: [LinkCreateDialog, FormsModule],
      providers: [{ provide: FilesApiService, useValue: api }],
    });
  });

  function create(fileId = 'file-1'): LinkCreateDialog {
    const fixture = TestBed.createComponent(LinkCreateDialog);
    fixture.componentRef.setInput('fileId', fileId);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('creates a link and exposes the download key exactly once in the result', async () => {
    api.createLink.and.resolveTo(createdLink());
    const dialog = create();
    const created = jasmine.createSpy('created');
    dialog.created.subscribe(created);

    await dialog.create();

    expect(api.createLink).toHaveBeenCalledWith('file-1', undefined);
    expect(dialog.result()?.downloadKey).toBe('Ab3xY9Qz');
    expect(dialog.result()?.url).toBe('/d/tok');
  });

  it('rejects an invalid max downloads value without calling the API', async () => {
    const dialog = create();
    dialog.maxDownloads = '0';

    await dialog.create();

    expect(api.createLink).not.toHaveBeenCalled();
    expect(dialog.error()).toBeTruthy();
  });

  it('passes the parsed max downloads through to the API', async () => {
    api.createLink.and.resolveTo(createdLink({ maxDownloads: 3 }));
    const dialog = create();
    dialog.maxDownloads = '3';

    await dialog.create();

    expect(api.createLink).toHaveBeenCalledWith('file-1', 3);
  });
});

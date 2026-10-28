import { TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { FileEditDialog } from './file-edit-dialog';
import { FilesApiService } from '../../core/api/files-api.service';
import type { FileMetadata } from '../../core/models/file-metadata';

function metadata(overrides: Partial<FileMetadata> = {}): FileMetadata {
  return {
    id: 'file-1',
    fileName: 'report.pdf',
    sizeBytes: 1024,
    contentType: 'application/pdf',
    status: 'available',
    accessMode: 'owner_only',
    retention: { mode: 'finite', days: 30, expiresAt: null, reason: null },
    tags: ['finance'],
    downloads: { count: 0, lastAt: null },
    scanStatus: 'clean',
    legalHold: false,
    legalHoldReason: null,
    legalHoldReviewAt: null,
    owner: { type: 'human', displayName: null },
    quarantineReason: null,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    availableAt: new Date().toISOString(),
    deletedAt: null,
    version: 3,
    etag: '"3"',
    ...overrides,
  };
}

describe('FileEditDialog', () => {
  let api: jasmine.SpyObj<FilesApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<FilesApiService>('FilesApiService', ['update']);
    TestBed.configureTestingModule({
      imports: [FileEditDialog, FormsModule],
      providers: [{ provide: FilesApiService, useValue: api }],
    });
  });

  function create(file: FileMetadata = metadata()): FileEditDialog {
    const fixture = TestBed.createComponent(FileEditDialog);
    fixture.componentRef.setInput('file', file);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('sends rename, tags, and retention with the file ETag', async () => {
    api.update.and.resolveTo(metadata({ fileName: 'renamed.pdf' }));
    const dialog = create();
    const saved = jasmine.createSpy('saved');
    dialog.saved.subscribe(saved);

    dialog.fileName = 'renamed.pdf';
    dialog.tags = 'finance, q3';
    dialog.days = 30;
    await dialog.save();

    expect(api.update).toHaveBeenCalledWith(
      'file-1',
      jasmine.objectContaining({
        fileName: 'renamed.pdf',
        tags: ['finance', 'q3'],
        retention: { mode: 'finite', days: 30, reason: undefined },
      }),
      '"3"',
    );
    expect(saved).toHaveBeenCalled();
  });

  it('requires a note of at least 10 characters when extending beyond the default', async () => {
    const dialog = create();
    dialog.days = 365;
    dialog.note = 'short';
    await dialog.save();

    expect(api.update).not.toHaveBeenCalled();
    expect(dialog.error()).toBeTruthy();
  });

  it('surfaces the stale_version problem inline', async () => {
    api.update.and.rejectWith({ error: { code: 'stale_version' } });
    const dialog = create();
    await dialog.save();

    expect(dialog.error()).toContain('cambió');
  });

  it('rejects an empty file name before calling the API', async () => {
    const dialog = create();
    dialog.fileName = '   ';
    await dialog.save();

    expect(api.update).not.toHaveBeenCalled();
    expect(dialog.error()).toBeTruthy();
  });
});

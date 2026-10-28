import { TestBed } from '@angular/core/testing';
import { FormsModule } from '@angular/forms';
import { FileDeleteDialog } from './file-delete-dialog';
import { FilesApiService } from '../../core/api/files-api.service';
import type { FileMetadata } from '../../core/models/file-metadata';

function metadata(downloads: number): FileMetadata {
  return {
    id: 'file-1',
    fileName: 'report.pdf',
    sizeBytes: 1024,
    contentType: 'application/pdf',
    status: 'available',
    accessMode: 'owner_only',
    retention: { mode: 'finite', days: 30, expiresAt: null, reason: null },
    tags: [],
    downloads: { count: downloads, lastAt: null },
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
    version: 1,
    etag: '"1"',
  };
}

describe('FileDeleteDialog', () => {
  let api: jasmine.SpyObj<FilesApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<FilesApiService>('FilesApiService', ['delete']);
    TestBed.configureTestingModule({
      imports: [FileDeleteDialog, FormsModule],
      providers: [{ provide: FilesApiService, useValue: api }],
    });
  });

  function create(file: FileMetadata): FileDeleteDialog {
    const fixture = TestBed.createComponent(FileDeleteDialog);
    fixture.componentRef.setInput('file', file);
    fixture.detectChanges();
    return fixture.componentInstance;
  }

  it('deletes without typing when the file has no downloads', async () => {
    api.delete.and.resolveTo(undefined);
    const dialog = create(metadata(0));
    const deleted = jasmine.createSpy('deleted');
    dialog.deleted.subscribe(deleted);

    await dialog.confirm();

    expect(api.delete).toHaveBeenCalledWith('file-1');
    expect(deleted).toHaveBeenCalled();
  });

  it('requires type-to-confirm when the file has downloads', async () => {
    const dialog = create(metadata(5));
    await dialog.confirm();
    expect(api.delete).not.toHaveBeenCalled();
    expect(dialog.error()).toBeTruthy();

    dialog.confirmName = 'report.pdf';
    await dialog.confirm();
    expect(api.delete).toHaveBeenCalled();
  });

  it('surfaces the legal_hold problem inline', async () => {
    api.delete.and.rejectWith({ error: { code: 'legal_hold_active' } });
    const dialog = create(metadata(0));
    await dialog.confirm();

    expect(dialog.error()).toContain('retención legal');
  });
});

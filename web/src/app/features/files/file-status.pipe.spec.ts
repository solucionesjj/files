import { FileStatusBadgePipe, FileStatusLabelPipe } from './file-status.pipe';

describe('FileStatusLabelPipe', () => {
  const pipe = new FileStatusLabelPipe();

  it('returns a Spanish label for each known status', () => {
    expect(pipe.transform('available')).toBe('Disponible');
    expect(pipe.transform('processing')).toBe('Procesando');
    expect(pipe.transform('quarantined')).toBe('En cuarentena');
    expect(pipe.transform('expired')).toBe('Expirado');
    expect(pipe.transform('deleted')).toBe('Eliminado');
  });
});

describe('FileStatusBadgePipe', () => {
  const pipe = new FileStatusBadgePipe();

  it('maps statuses to distinct badge colors', () => {
    expect(pipe.transform('available')).toBe('text-bg-success');
    expect(pipe.transform('quarantined')).toBe('text-bg-danger');
    expect(pipe.transform('processing')).toBe('text-bg-warning');
    expect(pipe.transform('deleted')).toBe('text-bg-dark');
  });
});

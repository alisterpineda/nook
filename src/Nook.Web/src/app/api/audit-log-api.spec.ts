import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuditLogApi } from './audit-log-api';

describe('AuditLogApi', () => {
  let api: AuditLogApi;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(AuditLogApi);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpTesting.verify());

  it('lists entries with the filters given', async () => {
    const page = { items: [], total: 0, offset: 50, limit: 50 };

    const listing = api.list({
      Offset: 50,
      Limit: 50,
      ActorId: '0198a1b2-0000-7000-8000-000000000001',
      Action: 'job.retried',
    });
    const request = httpTesting.expectOne((candidate) => candidate.url === '/api/audit-log');
    expect(request.request.method).toBe('GET');
    expect(request.request.params.get('Offset')).toBe('50');
    expect(request.request.params.get('Limit')).toBe('50');
    expect(request.request.params.get('ActorId')).toBe('0198a1b2-0000-7000-8000-000000000001');
    expect(request.request.params.get('Action')).toBe('job.retried');
    request.flush(page);

    expect(await listing).toEqual(page);
  });

  it('leaves out a filter that is not given', async () => {
    const listing = api.list({ Offset: 0, Limit: 50 });
    const request = httpTesting.expectOne((candidate) => candidate.url === '/api/audit-log');
    expect(request.request.params.has('ActorId')).toBe(false);
    expect(request.request.params.has('Action')).toBe(false);
    request.flush({ items: [], total: 0, offset: 0, limit: 50 });

    await listing;
  });

  it('asks for the people and actions to filter by', async () => {
    const filters = {
      actors: [
        { id: '0198a1b2-0000-7000-8000-000000000001', name: 'Ada', email: 'ada@example.com' },
      ],
      actions: ['job.retried', 'upload.added'],
    };

    const asking = api.filters();
    const request = httpTesting.expectOne('/api/audit-log/filters');
    expect(request.request.method).toBe('GET');
    request.flush(filters);

    expect(await asking).toEqual(filters);
  });
});

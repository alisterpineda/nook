import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { components } from '../../api/schema';
import { FakeActivityHub, provideFakeActivityHub } from '../../testing/fake-activity-hub';
import { uploadActivity as upload } from '../../testing/upload-fixtures';
import { UploadActivity } from './upload-activity';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];

describe('UploadActivity', () => {
  let httpTesting: HttpTestingController;
  let hub: FakeActivityHub;
  let store: UploadActivity;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideFakeActivityHub()],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(FakeActivityHub);
    store = TestBed.inject(UploadActivity);
  });

  afterEach(() => httpTesting.verify());

  const names = () => store.rows().map((row) => row.fileName);

  /** Starts the store and answers its list with `uploads`. */
  async function startWith(uploads: UploadActivityResponse[]): Promise<void> {
    store.start();
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'));
    request.flush(uploads);
    await vi.waitFor(() => expect(store.rows()).toEqual(uploads));
  }

  it('lists the uploads the server holds', async () => {
    await startWith([upload(2), upload(1)]);

    expect(names()).toEqual(['book-2.epub', 'book-1.epub']);
    expect(hub.open).toBe(true);
  });

  it('changes a row when the hub says it changed', async () => {
    await startWith([upload(2), upload(1)]);

    hub.uploadChanged.next(upload(1, 'added'));

    expect(store.rows().map((row) => row.status)).toEqual(['queued', 'added']);
    expect(names()).toEqual(['book-2.epub', 'book-1.epub']);
  });

  it('adds a row the hub names for the first time', async () => {
    await startWith([upload(2), upload(1)]);

    hub.uploadChanged.next(upload(3));

    expect(names()).toEqual(['book-3.epub', 'book-2.epub', 'book-1.epub']);
  });

  it('keeps at most the last twenty rows', async () => {
    await startWith(Array.from({ length: 20 }, (_, i) => upload(20 - i)));

    hub.uploadChanged.next(upload(21));

    expect(store.rows()).toHaveLength(20);
    expect(store.rows()[0].fileName).toBe('book-21.epub');
    expect(store.rows().at(-1)?.fileName).toBe('book-2.epub');
  });

  it('drops a row the hub says was removed', async () => {
    await startWith([upload(2), upload(1)]);

    hub.uploadRemoved.next(upload(2).jobId);

    expect(names()).toEqual(['book-1.epub']);
  });

  it('asks for the list again after a reconnect', async () => {
    await startWith([upload(1)]);

    hub.reconnected.next();
    const request = httpTesting.expectOne('/api/activity/uploads');
    request.flush([upload(2, 'added'), upload(1, 'added')]);

    await vi.waitFor(() => expect(names()).toEqual(['book-2.epub', 'book-1.epub']));
  });

  it('keeps what the hub said while the list was on its way', async () => {
    await startWith([upload(2, 'running'), upload(1, 'failed')]);

    hub.reconnected.next();
    const request = httpTesting.expectOne('/api/activity/uploads');
    hub.uploadChanged.next(upload(2, 'added'));
    hub.uploadRemoved.next(upload(1).jobId);
    hub.uploadChanged.next(upload(3));
    request.flush([upload(2, 'running'), upload(1, 'failed')]);

    await vi.waitFor(() => expect(names()).toEqual(['book-3.epub', 'book-2.epub']));
    expect(store.rows().map((row) => row.status)).toEqual(['queued', 'added']);
  });

  it('empties the list and closes the hub when stopped', async () => {
    await startWith([upload(1)]);

    await store.stop();

    expect(store.rows()).toEqual([]);
    expect(hub.open).toBe(false);
    hub.uploadChanged.next(upload(2));
    expect(store.rows()).toEqual([]);
  });

  it('puts no rows back when the list answers after the store was stopped', async () => {
    store.start();
    const request = await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'));

    await store.stop();
    request.flush([upload(1)]);
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(store.rows()).toEqual([]);
  });

  it('asks for no list when stopped before the hub has opened', async () => {
    store.start();
    await store.stop();
    await new Promise((resolve) => setTimeout(resolve, 0));

    httpTesting.expectNone('/api/activity/uploads');
    expect(store.rows()).toEqual([]);
  });

  it('ignores an older list that answers after a newer one', async () => {
    await startWith([upload(1)]);

    hub.reconnected.next();
    const older = httpTesting.expectOne('/api/activity/uploads');
    hub.reconnected.next();
    const newer = httpTesting.expectOne('/api/activity/uploads');

    newer.flush([upload(3), upload(2)]);
    await vi.waitFor(() => expect(names()).toEqual(['book-3.epub', 'book-2.epub']));
    older.flush([upload(1)]);
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(names()).toEqual(['book-3.epub', 'book-2.epub']);
  });
});

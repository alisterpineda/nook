import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { components } from '../../api/schema';
import { provideFakeActivityHub } from '../../testing/fake-activity-hub';
import { signInAs } from '../../testing/sign-in-as';
import { epub, sendTwoFiles, uploadActivity as upload } from '../../testing/upload-fixtures';
import { UploadQueue } from '../upload/upload-queue';
import { UploadActivity } from './upload-activity';
import { ActivityButton } from './activity-button';

type UploadActivityResponse = components['schemas']['UploadActivityResponse'];
type CurrentUserResponse = components['schemas']['CurrentUserResponse'];

describe('ActivityButton', () => {
  const workId = '0198a1b2-0000-7000-8000-0000000000aa';
  const uploader: CurrentUserResponse = {
    id: '0198a1b2-0000-7000-8000-000000000002',
    email: 'grace@example.com',
    role: 'contributor',
    displayName: 'Grace Hopper',
    canUpload: true,
  };

  let httpTesting: HttpTestingController;
  let queue: UploadQueue;
  let fixture: ComponentFixture<ActivityButton>;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideFakeActivityHub(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    queue = TestBed.inject(UploadQueue);
    await signInAs(uploader);
    fixture = TestBed.createComponent(ActivityButton);
    await fixture.whenStable();
  });

  afterEach(() => {
    // Close any open popover so the next test starts with a clean page.
    fixture.destroy();
    httpTesting.verify({ ignoreCancelled: true });
  });

  /** Gives the Notifications list `uploads` from the server. */
  async function listUploads(uploads: UploadActivityResponse[]): Promise<void> {
    const activity = TestBed.inject(UploadActivity);
    activity.start();
    (await vi.waitFor(() => httpTesting.expectOne('/api/activity/uploads'))).flush(uploads);
    await vi.waitFor(() => expect(activity.rows()).toEqual(uploads));
    await fixture.whenStable();
  }

  async function openPopover(): Promise<HTMLElement> {
    trigger()!.click();
    await fixture.whenStable();
    return vi.waitFor(() => {
      const content = document.querySelector<HTMLElement>('[data-slot="popover-content"]');
      if (!content) {
        throw new Error('The popover is not open.');
      }
      return content;
    });
  }

  function trigger(): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>(
      '[data-slot="popover-trigger"]',
    );
  }

  function badge(): HTMLElement | null {
    return trigger()!.querySelector<HTMLElement>('[data-slot="badge"]');
  }

  it('counts the uploads in flight on its badge', async () => {
    expect(badge()).toBeNull();

    await listUploads([upload(1, 'queued'), upload(2, 'running'), upload(3, 'added')]);
    sendTwoFiles(10);
    await fixture.whenStable();

    expect(badge()?.textContent?.trim()).toBe('4');
    expect(badge()?.getAttribute('data-variant')).toBe('default');
  });

  it('turns the badge to the failed count when any upload has failed', async () => {
    await listUploads([
      upload(1, 'queued'),
      upload(2, 'failed', { failure: 'internal' }),
      upload(3, 'failed', { failure: 'unreadable' }),
    ]);
    queue.add([epub('dune.epub')], null);
    httpTesting
      .expectOne('/api/uploads')
      .flush({ title: 'Bad Request', status: 400 }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();

    expect(badge()?.textContent?.trim()).toBe('3');
    expect(badge()?.getAttribute('data-variant')).toBe('destructive');
  });

  it('counts every failed upload on its badge, past twenty', async () => {
    await listUploads([
      ...Array.from({ length: 25 }, (_, i) => upload(26 - i, 'failed', { failure: 'internal' })),
      upload(1, 'queued'),
    ]);

    expect(badge()?.textContent?.trim()).toBe('25');
    expect(badge()?.getAttribute('data-variant')).toBe('destructive');
  });

  it('shows no badge with nothing in flight and nothing failed', async () => {
    await listUploads([upload(1, 'added', { workId })]);

    expect(badge()).toBeNull();
  });

  it('holds the uploads in a section of its popover', async () => {
    await listUploads([upload(1, 'added', { workId })]);

    const popover = await openPopover();

    expect(popover.querySelector('h2')?.textContent).toBe('Notifications');
    const section = popover.querySelector('app-uploads-section');
    expect(section?.querySelector('h3')?.textContent).toBe('Uploads');
    expect(section?.textContent).toContain('book-1.epub');
  });

  it('leaves the uploads out and says there is nothing yet when there are none', async () => {
    const popover = await openPopover();

    expect(popover.querySelector('app-uploads-section')).toBeNull();
    expect(popover.textContent).toContain('No notifications yet.');
  });

  it('is not there for someone with no section to see', async () => {
    await signInAs({ ...uploader, role: 'viewer', canUpload: false });
    await fixture.whenStable();

    expect(trigger()).toBeNull();
  });
});

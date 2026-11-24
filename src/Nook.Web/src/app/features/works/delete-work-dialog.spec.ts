import { Location } from '@angular/common';
import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { menuItemLabelled } from '../../testing/menu-item-labelled';
import { WorkDetailPage } from './work-detail-page';

type WorkDetailResponse = components['schemas']['WorkDetailResponse'];

@Component({ template: `<h1>Library</h1>` })
class LibraryStub {}

describe('DeleteWorkDialog', () => {
  const dune: WorkDetailResponse = {
    id: '0198a1b2-0000-7000-8000-000000000001',
    sectionId: '0198a1b2-0000-7000-8000-00000000000a',
    title: 'Dune',
    sortTitle: 'Dune',
    description: null,
    language: null,
    publicationDate: null,
    publisher: null,
    series: null,
    coverHash: null,
    contributors: [],
    tags: [],
    externalIds: [],
    items: [],
    addedAt: '2026-11-01T12:00:00Z',
    updatedAt: '2026-11-02T12:00:00Z',
    access: 'download',
    canEdit: true,
    addedBy: null,
  };

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', component: LibraryStub },
          { path: 'works/:id', component: WorkDetailPage },
        ]),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => {
    document
      .querySelectorAll('.cdk-overlay-container')
      .forEach((overlay) => overlay.replaceChildren());
    httpTesting.verify();
  });

  async function settle(): Promise<void> {
    await TestBed.inject(ApplicationRef).whenStable();
    TestBed.tick();
  }

  /** Opens the work page, chooses Delete from its menu, and returns the dialog. */
  async function openDialog(): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(`/works/${dune.id}`);
    (await vi.waitFor(() => httpTesting.expectOne(`/api/works/${dune.id}`))).flush(dune);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('h1')).not.toBeNull());
    page.querySelector<HTMLElement>('button[aria-label="More actions"]')!.click();
    (await vi.waitFor(() => menuItemLabelled('Delete'))).click();
    return vi.waitFor(() => {
      const found = document.querySelector<HTMLElement>('app-delete-work-dialog');
      expect(found).not.toBeNull();
      return found!;
    });
  }

  it('names the work and says deleting cannot be undone', async () => {
    const dialog = await openDialog();

    expect(text(dialog)).toContain('Delete this book?');
    expect(text(dialog)).toContain('Dune');
    expect(text(dialog)).toContain('This cannot be undone.');
  });

  it('deletes the work and goes to the library', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Delete').click();
    const request = await vi.waitFor(() => httpTesting.expectOne(`/api/works/${dune.id}`));
    expect(request.request.method).toBe('DELETE');
    await settle();
    expect(buttonLabelled(dialog, 'Delete').disabled).toBe(true);
    request.flush(null, { status: 204, statusText: 'No Content' });

    await vi.waitFor(() => expect(TestBed.inject(Location).path()).toBe('/library'));
    await settle();
    expect(document.querySelector('app-delete-work-dialog')).toBeNull();
  });

  it('sends nothing when cancelled', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Cancel').click();
    await settle();

    expect(document.querySelector('app-delete-work-dialog')).toBeNull();
    expect(TestBed.inject(Location).path()).toBe(`/works/${dune.id}`);
  });

  it('says so and stays open when the delete fails', async () => {
    const dialog = await openDialog();

    buttonLabelled(dialog, 'Delete').click();
    (await vi.waitFor(() => httpTesting.expectOne(`/api/works/${dune.id}`))).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await settle();

    expect(text(dialog.querySelector('[role="alert"]'))).toBe('Something went wrong. Try again.');
    expect(document.querySelector('app-delete-work-dialog')).not.toBeNull();
    expect(buttonLabelled(dialog, 'Delete').disabled).toBe(false);
  });
});

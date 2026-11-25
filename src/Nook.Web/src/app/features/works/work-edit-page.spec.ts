import { provideLocationMocks } from '@angular/common/testing';
import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  TestRequest,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { components } from '../../api/schema';
import { PageName } from '../../core/page-name';
import { somethingWentWrong } from '../../core/messages';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { fieldLabelled } from '../../testing/field-labelled';
import { groupLabelled } from '../../testing/group-labelled';
import { typeInto } from '../../testing/type-into';
import { WorkEditPage } from './work-edit-page';

type SectionResponse = components['schemas']['SectionResponse'];
type WorkDetailResponse = components['schemas']['WorkDetailResponse'];
type WorkEditRequest = components['schemas']['WorkEditRequest'];

const duneId = '0198a1b2-0000-7000-8000-000000000001';
const novelsId = '0198a1b2-0000-7000-8000-00000000000a';
const classicsId = '0198a1b2-0000-7000-8000-00000000000b';

@Component({ template: `<h1>Work</h1>` })
class WorkStub {}

describe('WorkEditPage', () => {
  const dune: WorkDetailResponse = {
    id: duneId,
    sectionId: novelsId,
    title: 'Dune',
    sortTitle: 'Dune',
    description: 'A desert planet.',
    language: 'en',
    publicationDate: null,
    publisher: null,
    series: { id: '0198a1b2-0000-7000-8000-0000000000c1', name: 'Dune Chronicles', position: 1 },
    coverHash: null,
    contributors: [
      { personId: '0198a1b2-0000-7000-8000-0000000000d1', name: 'Frank Herbert', role: 'author' },
      { personId: '0198a1b2-0000-7000-8000-0000000000d2', name: 'Scott Brick', role: 'narrator' },
      { personId: '0198a1b2-0000-7000-8000-0000000000d3', name: 'Brian Herbert', role: 'author' },
    ],
    tags: [
      { id: '0198a1b2-0000-7000-8000-0000000000e1', name: 'Science fiction' },
      { id: '0198a1b2-0000-7000-8000-0000000000e2', name: 'Classics, old' },
    ],
    externalIds: [],
    items: [],
    addedAt: '2026-11-01T12:00:00Z',
    updatedAt: '2026-11-02T12:00:00Z',
    access: 'download',
    canEdit: true,
    addedBy: null,
  };
  const sections: SectionResponse[] = [
    { id: novelsId, name: 'Novels' },
    { id: classicsId, name: 'Classics' },
  ] as SectionResponse[];

  let harness: RouterTestingHarness;
  let httpTesting: HttpTestingController;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'works/:id/edit', component: WorkEditPage },
          { path: 'works/:id', component: WorkStub },
        ]),
        provideLocationMocks(),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    httpTesting = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => httpTesting.verify());

  function request(url: string): Promise<TestRequest> {
    return vi.waitFor(() => httpTesting.expectOne(url));
  }

  /** Opens the edit page for the work and answers its two requests. */
  async function openEdit(work: WorkDetailResponse): Promise<HTMLElement> {
    const navigating = harness.navigateByUrl(`/works/${work.id}/edit`);
    (await request(`/api/works/${work.id}`)).flush(work);
    (await request('/api/sections')).flush(sections);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('form')).not.toBeNull());
    return page;
  }

  function valuesOf(page: HTMLElement, label: string): string[] {
    return Array.from(page.querySelectorAll(`[role="group"][aria-label^="${label} "] input`)).map(
      (input) => (input as HTMLInputElement).value,
    );
  }

  async function submit(page: HTMLElement): Promise<void> {
    buttonLabelled(page, 'Save').click();
    await harness.fixture.whenStable();
  }

  it("fills the form with the work's values", async () => {
    const page = await openEdit(dune);

    expect(fieldLabelled(page, 'Title').value).toBe('Dune');
    expect(fieldLabelled(page, 'Sort title').value).toBe('Dune');
    expect(valuesOf(page, 'Author')).toEqual(['Frank Herbert', 'Brian Herbert']);
    expect(fieldLabelled(page, 'Series').value).toBe('Dune Chronicles');
    expect(fieldLabelled(page, 'Position in the series').value).toBe('1');
    expect(valuesOf(page, 'Tag')).toEqual(['Science fiction', 'Classics, old']);
    expect(fieldLabelled(page, 'Description', HTMLTextAreaElement).value).toBe('A desert planet.');
    const section = fieldLabelled(page, 'Section', HTMLSelectElement);
    expect(Array.from(section.options).map(text)).toEqual(['Default', 'Novels', 'Classics']);
    expect(section.value).toBe(novelsId);
  });

  it('keeps the section of a work whose section is not among those listed', async () => {
    const elsewhereId = '0198a1b2-0000-7000-8000-0000000000ee';
    const page = await openEdit({ ...dune, sectionId: elsewhereId });

    const section = fieldLabelled(page, 'Section', HTMLSelectElement);
    expect(Array.from(section.options).map(text)).toEqual([
      'Default',
      'Current section',
      'Novels',
      'Classics',
    ]);
    expect(section.value).toBe(elsewhereId);
    await submit(page);

    const saving = await request(`/api/works/${dune.id}`);
    expect(saving.request.body.sectionId).toBe(elsewhereId);
    saving.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => expect(router.url).toBe(`/works/${dune.id}`));
  });

  it('saves the edited values and returns to the work', async () => {
    const page = await openEdit(dune);

    typeInto(fieldLabelled(page, 'Title'), 'Dune Messiah');
    buttonLabelled(groupLabelled(page, 'Author 2'), 'Move up').click();
    const section = fieldLabelled(page, 'Section', HTMLSelectElement);
    section.value = classicsId;
    section.dispatchEvent(new Event('change'));
    await submit(page);

    const put = await request(`/api/works/${duneId}`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({
      title: 'Dune Messiah',
      sortTitle: 'Dune',
      authors: ['Brian Herbert', 'Frank Herbert'],
      series: { name: 'Dune Chronicles', position: 1 },
      tags: ['Science fiction', 'Classics, old'],
      description: 'A desert planet.',
      sectionId: classicsId,
    } satisfies WorkEditRequest);
    put.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => expect(router.url).toBe(`/works/${duneId}`));
    expect(TestBed.inject(PageName).of(duneId)).toBe('Dune Messiah');
  });

  it('sends no series when the series name is emptied', async () => {
    const page = await openEdit(dune);

    typeInto(fieldLabelled(page, 'Series'), '');
    await submit(page);

    const put = await request(`/api/works/${duneId}`);
    expect((put.request.body as WorkEditRequest).series).toBeNull();
    put.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('leaves out an author field left empty', async () => {
    const page = await openEdit(dune);

    buttonLabelled(page, 'Add').click();
    harness.detectChanges();
    await submit(page);

    const put = await request(`/api/works/${duneId}`);
    expect((put.request.body as WorkEditRequest).authors).toEqual([
      'Frank Herbert',
      'Brian Herbert',
    ]);
    put.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('says so and stays when the save fails', async () => {
    const page = await openEdit(dune);

    await submit(page);
    (await request(`/api/works/${duneId}`)).flush(
      { title: 'Bad Request', status: 400 },
      { status: 400, statusText: 'Bad Request' },
    );
    await vi.waitFor(() => expect(page.querySelector('[role="alert"]')).not.toBeNull());

    expect(text(page.querySelector('[role="alert"]'))).toBe(somethingWentWrong);
    expect(router.url).toBe(`/works/${duneId}/edit`);
    expect(fieldLabelled(page, 'Title').value).toBe('Dune');
  });

  it('does not save while the title is empty', async () => {
    const page = await openEdit(dune);

    typeInto(fieldLabelled(page, 'Title'), '');
    await submit(page);
    harness.detectChanges();

    httpTesting.expectNone(`/api/works/${duneId}`);
    expect(text(page.querySelector('[role="alert"]'))).toBe('Enter a title.');
  });

  it('sends someone who may not edit to the work page', async () => {
    const navigating = harness.navigateByUrl(`/works/${duneId}/edit`);
    (await request(`/api/works/${duneId}`)).flush({ ...dune, canEdit: false });
    (await request('/api/sections')).flush(sections);
    await navigating;

    await vi.waitFor(() => expect(router.url).toBe(`/works/${duneId}`));
  });

  it('says the work was not found when there is none', async () => {
    const navigating = harness.navigateByUrl('/works/missing/edit');
    (await request('/api/works/missing')).flush(
      { title: 'Not Found', status: 404 },
      { status: 404, statusText: 'Not Found' },
    );
    (await request('/api/sections')).flush(sections);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('h1')).not.toBeNull());

    expect(text(page.querySelector('h1'))).toBe('Work not found');
    expect(TestBed.inject(PageName).of('missing')).toBe('Work not found');
  });

  it('offers a retry when the work cannot be loaded', async () => {
    const navigating = harness.navigateByUrl(`/works/${duneId}/edit`);
    (await request(`/api/works/${duneId}`)).flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    (await request('/api/sections')).flush(sections);
    await navigating;
    const page = harness.routeNativeElement as HTMLElement;
    await vi.waitFor(() => expect(page.querySelector('[role="alert"]')).not.toBeNull());

    buttonLabelled(page, 'Retry').click();
    (await request(`/api/works/${duneId}`)).flush(dune);
    (await request('/api/sections')).flush(sections);
    await vi.waitFor(() => expect(page.querySelector('form')).not.toBeNull());
  });
});

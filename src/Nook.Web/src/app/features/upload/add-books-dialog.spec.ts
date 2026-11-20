import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationRef } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { components } from '../../api/schema';
import { buttonLabelled } from '../../testing/button-labelled';
import { text } from '../../testing/element-text';
import { epub } from '../../testing/upload-fixtures';
import { AddBooks } from './add-books';
import { UploadQueue } from './upload-queue';

type SectionResponse = components['schemas']['SectionResponse'];

describe('AddBooksDialog', () => {
  const books: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000a', name: 'Books' };
  const comics: SectionResponse = { id: '0198a1b2-0000-7000-8000-00000000000b', name: 'Comics' };
  const limit = 1024;

  let addBooks: AddBooks;
  let queue: UploadQueue;
  let httpTesting: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(withXhr()), provideHttpClientTesting()],
    });
    addBooks = TestBed.inject(AddBooks);
    queue = TestBed.inject(UploadQueue);
    httpTesting = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // Cancels the upload a test left open, and closes the dialog a test left open.
    queue.reset();
    document
      .querySelectorAll('.cdk-overlay-container')
      .forEach((overlay) => overlay.replaceChildren());
    httpTesting.verify({ ignoreCancelled: true });
  });

  async function settle(): Promise<void> {
    await TestBed.inject(ApplicationRef).whenStable();
    TestBed.tick();
  }

  /**
   * Opens the dialog and answers its requests: `sections` for the sections, and `maxBytes` for the size limit, or a
   * failure when it is null.
   */
  async function openDialog(
    files: File[],
    sectionParam: string | null,
    sections: SectionResponse[] = [],
    maxBytes: number | null = limit,
  ): Promise<HTMLElement> {
    addBooks.open(files, sectionParam);
    const sectionsRequest = await vi.waitFor(() => httpTesting.expectOne('/api/sections'));
    sectionsRequest.flush(sections);
    const limits = httpTesting.expectOne('/api/uploads/limits');
    if (maxBytes === null) {
      limits.flush(null, { status: 500, statusText: 'Server Error' });
    } else {
      limits.flush({ maxBytes });
    }
    await settle();
    return dialog();
  }

  function dialog(): HTMLElement {
    const found = document.querySelector<HTMLElement>('app-add-books-dialog');
    if (!found) {
      throw new Error('No dialog is open');
    }
    return found;
  }

  function fileNames(element: HTMLElement): string[] {
    return Array.from(element.querySelectorAll('[data-file-name]'), (name) => text(name));
  }

  function picker(element: HTMLElement): HTMLSelectElement {
    return element.querySelector('select')!;
  }

  function offered(element: HTMLElement): string[] {
    return Array.from(picker(element).options, (option) => option.text.trim());
  }

  function chosen(element: HTMLElement): string {
    return picker(element).selectedOptions[0]?.text.trim() ?? '';
  }

  function rowOf(element: HTMLElement, name: string): HTMLElement {
    const found = Array.from(element.querySelectorAll<HTMLElement>('li')).find(
      (row) => text(row.querySelector('[data-file-name]')) === name,
    );
    if (!found) {
      throw new Error(`No row for ${name}`);
    }
    return found;
  }

  it('lists the files it was opened with', async () => {
    const element = await openDialog([epub('dune.epub'), epub('emma.epub')], null);

    expect(fileNames(element)).toEqual(['dune.epub', 'emma.epub']);
  });

  it('opens with an empty list and a file input when given no files', async () => {
    const element = await openDialog([], null);

    expect(fileNames(element)).toEqual([]);
    expect(element.querySelector('input[type="file"]')).not.toBeNull();
    expect(buttonLabelled(element, 'Add').disabled).toBe(true);
  });

  it('takes a file off the list', async () => {
    const element = await openDialog([epub('dune.epub'), epub('emma.epub')], null);

    rowOf(element, 'dune.epub').querySelector('button')!.click();
    await settle();

    expect(fileNames(element)).toEqual(['emma.epub']);
  });

  it('marks a file over the limit and leaves it out', async () => {
    const element = await openDialog([epub('dune.epub'), epub('huge.epub', limit + 1)], null);

    expect(text(rowOf(element, 'huge.epub'))).toContain('Too large');
    expect(text(rowOf(element, 'dune.epub'))).not.toContain('Too large');

    buttonLabelled(element, 'Add').click();
    await settle();

    expect(queue.rows().map((row) => row.name)).toEqual(['dune.epub']);
  });

  it('marks nothing when the limit cannot be fetched', async () => {
    const element = await openDialog([epub('huge.epub', limit + 1)], null, [], null);

    expect(text(element)).not.toContain('Too large');
    expect(buttonLabelled(element, 'Add').disabled).toBe(false);
  });

  it('offers Default and the sections the user can see', async () => {
    const element = await openDialog([], null, [books, comics]);

    expect(offered(element)).toEqual(['Default', 'Books', 'Comics']);
  });

  it('starts on the section being browsed', async () => {
    const element = await openDialog([], comics.id, [books, comics]);

    expect(chosen(element)).toBe('Comics');
  });

  it('starts on Default when the browsed section is not one the user can see', async () => {
    const element = await openDialog([], '0198a1b2-0000-7000-8000-0000000000ff', [books]);

    expect(chosen(element)).toBe('Default');
  });

  it('shows with no sections to choose from', async () => {
    const element = await openDialog([epub('dune.epub')], null, []);

    expect(offered(element)).toEqual(['Default']);
    expect(fileNames(element)).toEqual(['dune.epub']);
  });

  it('queues the files for the chosen section on Add', async () => {
    const dune = epub('dune.epub');
    const emma = epub('emma.epub');
    const element = await openDialog([dune, emma], books.id, [books, comics]);

    const select = picker(element);
    select.value = comics.id;
    select.dispatchEvent(new Event('change'));
    await settle();
    buttonLabelled(element, 'Add').click();
    await settle();

    expect(queue.rows().map((row) => [row.file, row.sectionId])).toEqual([
      [dune, comics.id],
      [emma, comics.id],
    ]);
    await vi.waitFor(() => expect(document.querySelector('app-add-books-dialog')).toBeNull());
  });

  it('queues files for the Default area when Default is chosen', async () => {
    const element = await openDialog([epub('dune.epub')], 'default', [books]);

    buttonLabelled(element, 'Add').click();

    expect(queue.rows().map((row) => row.sectionId)).toEqual([null]);
  });

  it('queues nothing on Cancel', async () => {
    const element = await openDialog([epub('dune.epub')], null);

    buttonLabelled(element, 'Cancel').click();

    expect(queue.rows()).toEqual([]);
    await vi.waitFor(() => expect(document.querySelector('app-add-books-dialog')).toBeNull());
  });

  it('opens one dialog at a time', async () => {
    await openDialog([], null);

    addBooks.open([], null);
    await settle();

    expect(document.querySelectorAll('app-add-books-dialog').length).toBe(1);
  });

  it('adds files to the open dialog when asked to open again', async () => {
    const element = await openDialog([epub('dune.epub')], null);

    addBooks.open([epub('emma.epub')], null);
    await settle();

    expect(document.querySelectorAll('app-add-books-dialog').length).toBe(1);
    expect(fileNames(element)).toEqual(['dune.epub', 'emma.epub']);
  });
});

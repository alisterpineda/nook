import { Location } from '@angular/common';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { fieldLabelled } from '../../testing/field-labelled';
import { pressEnter } from '../../testing/press-enter';
import { typeInto } from '../../testing/type-into';
import { SearchBox } from './search-box';

@Component({ imports: [SearchBox], template: '<app-search-box />' })
class LibraryStub {}

@Component({ template: '' })
class WorkStub {}

describe('SearchBox', () => {
  const pause = 300;
  let harness: RouterTestingHarness;
  let router: Router;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'library', component: LibraryStub },
          { path: 'works/:id', component: WorkStub },
        ]),
      ],
    });
    router = TestBed.inject(Router);
    harness = await RouterTestingHarness.create();
  });

  afterEach(() => vi.useRealTimers());

  /** Opens the page at `url` with the clock under the test's control from then on. */
  async function openWithClock(url: string): Promise<HTMLElement> {
    await harness.navigateByUrl(url);
    await harness.fixture.whenStable();
    vi.useFakeTimers();
    return harness.fixture.nativeElement;
  }

  function box(page: HTMLElement): HTMLInputElement {
    return fieldLabelled(page, 'Search the library');
  }

  async function wait(ms: number): Promise<void> {
    await vi.advanceTimersByTimeAsync(ms);
    harness.detectChanges();
  }

  function clearButton(page: HTMLElement): HTMLButtonElement | null {
    return page.querySelector<HTMLButtonElement>('button[aria-label="Clear search"]');
  }

  it('typing a term and pausing goes to /library?q=<term>', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), ' hail ');
    await wait(pause);

    expect(router.url).toBe('/library?q=hail');
  });

  it('goes nowhere before the pause is over', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'hail');
    await wait(pause - 1);

    expect(router.url).toBe('/library');
  });

  it('Enter searches at once and the pause does nothing more', async () => {
    const page = await openWithClock('/library');
    const navigated = vi.spyOn(router, 'navigate');

    typeInto(box(page), 'hail');
    pressEnter(box(page));
    await wait(0);
    expect(router.url).toBe('/library?q=hail');
    await wait(pause);

    expect(navigated).toHaveBeenCalledTimes(1);
  });

  it('goes nowhere for a box that is empty or holds only spaces', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), '   ');
    pressEnter(box(page));
    await wait(pause);

    expect(router.url).toBe('/library');
  });

  it('searching from a section leaves the section, sort and page out of the URL', async () => {
    const page = await openWithClock('/library?section=books&sort=title&page=2');

    typeInto(box(page), 'hail');
    await wait(pause);

    expect(router.url).toBe('/library?q=hail');
  });

  it('refining a term leaves one step back to the library', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'h');
    await wait(pause);
    typeInto(box(page), 'ha');
    await wait(pause);
    expect(router.url).toBe('/library?q=ha');
    TestBed.inject(Location).back();
    await wait(10);

    // The router is not listening to the browser's history in this set-up, so the history itself is read.
    expect(TestBed.inject(Location).path()).toBe('/library');
  });

  it('shows the term the URL names', async () => {
    const page = await openWithClock('/library?q=weir');

    expect(box(page).value).toBe('weir');
  });

  it('keeps a trailing space while the user types', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'hail ');
    await wait(pause);
    expect(router.url).toBe('/library?q=hail');
    await wait(0);

    expect(box(page).value).toBe('hail ');
  });

  it('keeps what was typed while an earlier search was still on its way', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'hail');
    vi.advanceTimersByTime(pause);
    typeInto(box(page), 'hail mary');
    await wait(0);
    expect(router.url).toBe('/library?q=hail');
    expect(box(page).value).toBe('hail mary');

    await wait(pause);
    expect(router.url).toBe('/library?q=hail%20mary');
  });

  it('shows the term again on coming back to a search', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'hail');
    await wait(pause);
    await router.navigateByUrl('/works/42');
    await wait(0);
    await router.navigateByUrl('/library?q=hail');
    await wait(0);

    expect(box(page).value).toBe('hail');
  });

  it('the clear button empties the box and shows the whole library', async () => {
    const page = await openWithClock('/library?q=weir');
    const navigated = vi.spyOn(router, 'navigate');

    clearButton(page)?.click();
    await wait(0);

    expect(box(page).value).toBe('');
    expect(router.url).toBe('/library');
    expect(navigated.mock.calls[0][1]).toMatchObject({ replaceUrl: true });
  });

  it('the clear button calls off a search that was waiting for the pause', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'hail');
    await wait(0);
    clearButton(page)?.click();
    await wait(pause);

    expect(box(page).value).toBe('');
    expect(router.url).toBe('/library');
  });

  it('emptying the box by hand shows the whole library', async () => {
    const page = await openWithClock('/library?q=weir');

    typeInto(box(page), '');
    await wait(pause - 1);
    expect(router.url).toBe('/library?q=weir');
    await wait(1);
    expect(router.url).toBe('/library');

    await router.navigateByUrl('/library?q=weir');
    await wait(0);
    typeInto(box(page), '');
    pressEnter(box(page));
    await wait(0);
    expect(router.url).toBe('/library');
  });

  it('has no clear button while the box is empty', async () => {
    const page = await openWithClock('/library');
    expect(clearButton(page)).toBeNull();

    typeInto(box(page), 'h');
    await wait(0);
    expect(clearButton(page)).not.toBeNull();

    typeInto(box(page), '');
    await wait(0);
    expect(clearButton(page)).toBeNull();
  });

  it('a box of only spaces shows the whole library and puts no q in the URL', async () => {
    const page = await openWithClock('/library?q=weir');

    typeInto(box(page), '   ');
    await wait(pause);

    expect(router.url).toBe('/library');
  });

  it('emptying the box where no search is showing goes nowhere', async () => {
    const page = await openWithClock('/library');

    typeInto(box(page), 'hail');
    await wait(0);
    typeInto(box(page), '');
    await wait(pause);
    expect(router.url).toBe('/library');

    typeInto(box(page), 'hail');
    await wait(0);
    clearButton(page)?.click();
    await wait(pause);
    expect(box(page).value).toBe('');
    expect(router.url).toBe('/library');
  });
});

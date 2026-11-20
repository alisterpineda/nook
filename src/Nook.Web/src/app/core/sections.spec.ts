import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Sections } from './sections';

describe('Sections', () => {
  it('keeps the latest answer when an earlier one arrives after it', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const sections = TestBed.inject(Sections);
    const http = TestBed.inject(HttpTestingController);

    const first = sections.load();
    const second = sections.load();
    const [early, late] = http.match('/api/sections');
    late.flush([{ id: 'b', name: 'Comics' }]);
    early.flush([{ id: 'a', name: 'Novels' }]);
    await Promise.all([first, second]);

    expect(sections.list().map((section) => section.name)).toEqual(['Comics']);
  });
});

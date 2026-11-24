import { HttpClient } from '@angular/common/http';
import { DOCUMENT, DestroyRef, Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ActivityHub } from './activity-hub';

const assetSelector = 'script[src], link[rel="stylesheet"][href]';

/**
 * Finds out that a newer build of the web app is being served. The built files carry a hash in their names, so a newer
 * build's `index.html` names a script or a stylesheet that the running page does not have. The page is requested when
 * the activity hub reconnects, as it does after a deployment restarts the API, and when the tab becomes visible again.
 * Once an update is available no further request is made. The hub is only listened to, never opened.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdate {
  private readonly destroyRef = inject(DestroyRef);
  private readonly document = inject(DOCUMENT);
  private readonly http = inject(HttpClient);
  private readonly hub = inject(ActivityHub);
  private readonly isAvailable = signal(false);

  /** Whether a newer build than the running one is being served. */
  readonly available = this.isAvailable.asReadonly();

  /** Starts checking when the hub reconnects and when the tab is shown again. */
  start(): void {
    const reconnected = this.hub.reconnected.subscribe(() => void this.check());
    const shown = () => {
      if (this.document.visibilityState === 'visible') {
        void this.check();
      }
    };
    this.document.addEventListener('visibilitychange', shown);
    this.destroyRef.onDestroy(() => {
      reconnected.unsubscribe();
      this.document.removeEventListener('visibilitychange', shown);
    });
  }

  /** Requests the served page and answers once it is compared with the running one; a failed request finds nothing. */
  async check(): Promise<void> {
    if (this.isAvailable()) {
      return;
    }
    let text: string;
    try {
      text = await firstValueFrom(this.http.get('/index.html', { responseType: 'text' }));
    } catch {
      return;
    }
    const served = new DOMParser().parseFromString(text, 'text/html');
    const running = new Set(namesIn(this.document));
    if (namesIn(served).some((name) => !running.has(name))) {
      this.isAvailable.set(true);
    }
  }

  /** Reloads the page, which loads the newer build. */
  reload(): void {
    this.document.defaultView?.location.reload();
  }
}

// The scripts' and stylesheets' addresses as written in the page.
function namesIn(page: Document): string[] {
  return Array.from(page.querySelectorAll(assetSelector), (element) =>
    element.getAttribute(element.localName === 'script' ? 'src' : 'href'),
  ).filter((name): name is string => name !== null);
}

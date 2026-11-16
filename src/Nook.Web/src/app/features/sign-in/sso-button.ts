import { Component, inject, signal } from '@angular/core';
import { HlmButtonImports } from '@spartan-ng/helm/button';
import { HlmCardImports } from '@spartan-ng/helm/card';
import { Session } from '../../core/session';

/**
 * A link that signs in through the single sign-on provider, for the bottom of a card. It asks the API once whether
 * there is a provider, and shows nothing, taking no space in the card, until it knows of one: while it waits, when
 * there is none, and when it could not find out.
 */
@Component({
  selector: 'app-sso-button',
  imports: [HlmButtonImports, HlmCardImports],
  host: { class: 'contents' },
  template: `
    @if (ssoName(); as name) {
      <div hlmCardContent>
        <!-- A plain href, so the browser leaves the app for the API, which sends it on to the provider. -->
        <a hlmBtn variant="outline" class="w-full" href="/api/auth/oidc/login"
          >Sign in with {{ name }}</a
        >
      </div>
    }
  `,
})
export class SsoButton {
  protected readonly ssoName = signal<string | null>(null);

  constructor() {
    inject(Session)
      .signInOptions()
      .then(
        (options) => this.ssoName.set(options.ssoName),
        // Without the options the password form still works, so a failure only means no link.
        () => this.ssoName.set(null),
      );
  }
}

import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { toParams } from './query-params';
import { components, paths } from './schema';

type AuditLogQuery = NonNullable<paths['/api/audit-log']['get']['parameters']['query']>;
type PageOfAuditEntryResponse = components['schemas']['PageOfAuditEntryResponse'];
type AuditFiltersResponse = components['schemas']['AuditFiltersResponse'];

@Injectable({ providedIn: 'root' })
export class AuditLogApi {
  private readonly http = inject(HttpClient);

  /** One page of the audit log, newest first, optionally only one person's entries and one action's. */
  list(query: AuditLogQuery): Promise<PageOfAuditEntryResponse> {
    return firstValueFrom(
      this.http.get<PageOfAuditEntryResponse>('/api/audit-log', { params: toParams(query) }),
    );
  }

  /** The people who have acted in the log and the actions it holds, to filter it by. */
  filters(): Promise<AuditFiltersResponse> {
    return firstValueFrom(this.http.get<AuditFiltersResponse>('/api/audit-log/filters'));
  }
}

import { components } from '../../api/schema';

type ContributorRole = components['schemas']['ContributorRole'];
type ItemFormat = components['schemas']['ItemFormat'];
type WorkSeriesResponse = components['schemas']['WorkSeriesResponse'];

const formatLabels: Partial<Record<ItemFormat, string>> = { epub: 'EPUB' };

const roleLabels: Partial<Record<ContributorRole, string>> = {
  author: 'Author',
  narrator: 'Narrator',
  translator: 'Translator',
  editor: 'Editor',
};

/** The label for a format, or the API's own value for one the app has no label for. */
export function formatLabel(format: ItemFormat): string {
  return formatLabels[format] ?? format;
}

/** The label for a contributor's role, or the API's own value for one the app has no label for. */
export function roleLabel(role: ContributorRole): string {
  return roleLabels[role] ?? role;
}

/** A series' name, followed by the work's position in it when it has one, such as "Dune #1". */
export function seriesText(series: WorkSeriesResponse | null): string {
  if (!series) {
    return '';
  }
  return series.position === null ? series.name : `${series.name} #${series.position}`;
}

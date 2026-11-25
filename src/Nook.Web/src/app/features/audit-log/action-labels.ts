/** How each action the audit log records reads in the list. */
const labels: Record<string, string> = {
  'upload.submitted': 'Submitted an upload',
  'upload.added': 'Upload added a book',
  'upload.duplicate': 'Upload was already in the library',
  'upload.failed': 'Upload failed',
  'job.retried': 'Retried a job',
  'job.deleted': 'Deleted a job',
  'public-access.changed': 'Changed public access',
  'work.edited': 'Edited a book',
  'work.deleted': 'Deleted a book',
  'user.password-changed': 'Changed their password',
};

/** The words for an audit action, or its own name when nothing words it. */
export function actionLabel(action: string): string {
  return labels[action] ?? action;
}

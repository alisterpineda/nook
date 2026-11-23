/** What one section of the Notifications popover has the button's badge say for it. */
export interface ActivitySummary {
  /** How many things it has to show. A section with none is left out of the popover. */
  items: number;
  /** How many of its things are under way. */
  inFlight: number;
  /** How many of its things failed and wait for the user to do something about them. */
  failed: number;
}

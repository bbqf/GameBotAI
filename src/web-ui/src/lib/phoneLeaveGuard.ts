/**
 * A guard for the phone back step (feature 185). A page with unsaved edits sets a guard.
 * The back handler of the app asks the guard before it leaves the page.
 * A browser cannot cancel a back step. When the guard says "block", the handler goes forward one step again.
 * The page then asks the operator what to do.
 */
type LeaveGuard = () => boolean;

let guard: LeaveGuard | null = null;

/** Set the guard, or remove it with null. A new guard replaces the old one. */
export const setLeaveGuard = (next: LeaveGuard | null): void => {
  guard = next;
};

/** True when the page has unsaved edits and the leave must wait for the operator. */
export const shouldBlockLeave = (): boolean => (guard ? guard() : false);

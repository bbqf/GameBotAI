import type { QueueDeviceRecoveryDto } from '../../services/queues';

/**
 * Form rules of the device recovery option (feature 199). The functions are pure, so a test can run them
 * with no render. The service keeps the same limits (feature 121, QueueDeviceRecoveryValidator).
 */

/** The values of a new queue: the option is off. The wait, the attempts, and the time between attempts match the service defaults. */
export const RECOVERY_DEFAULTS = { on: false, afterMinutes: 5, maxAttempts: 2, cooldownMinutes: 3 } as const;

/** The value of the form that belongs to device recovery. */
export type RecoveryFormFields = {
  /** The option: reboot the instance when the device is not live. */
  deviceRecoveryOn: boolean;
  /** The wait before the first reboot, in minutes. */
  recoveryAfterMinutes: number;
  /** The maximum attempts, from 1 to 5. */
  recoveryMaxAttempts: number;
  /** The time between attempts, in minutes. */
  recoveryCooldownMinutes: number;
  /** The stored value when the form opened. It is not shown. */
  storedDeviceRecovery: QueueDeviceRecoveryDto | null;
};

/** The field messages of the form. A key is set only when its field has a problem. */
export type RecoveryFieldErrors = {
  instanceName?: string;
  afterMinutes?: string;
  maxAttempts?: string;
  cooldownMinutes?: string;
};

/** The largest value of a 32-bit signed integer. The service stores the times in milliseconds in this type. */
const MAX_INT32 = 2147483647;
const MS_PER_MINUTE = 60000;
const MIN_AFTER_MS = 60000;
const MIN_ATTEMPTS = 1;
const MAX_ATTEMPTS = 5;

export const RECOVERY_INSTANCE_ERROR = 'Device recovery needs an emulator instance name.';
export const RECOVERY_AFTER_ERROR = 'Enter 1 minute or more.';
export const RECOVERY_ATTEMPTS_ERROR = 'Enter a whole number from 1 to 5.';
export const RECOVERY_COOLDOWN_ERROR = 'Enter 0 minutes or more.';

/** Converts milliseconds to minutes, rounded to one decimal place. */
export const msToMinutes = (ms: number): number => Math.round(ms / 6000) / 10;

/** Converts minutes to whole milliseconds. */
const minutesToMs = (minutes: number): number => Math.round(minutes * MS_PER_MINUTE);

/** Builds the form fields from the stored settings. Null or undefined gives the defaults. */
export const recoveryFormFromDto = (dto: QueueDeviceRecoveryDto | null | undefined): RecoveryFormFields => {
  if (!dto) {
    return {
      deviceRecoveryOn: RECOVERY_DEFAULTS.on,
      recoveryAfterMinutes: RECOVERY_DEFAULTS.afterMinutes,
      recoveryMaxAttempts: RECOVERY_DEFAULTS.maxAttempts,
      recoveryCooldownMinutes: RECOVERY_DEFAULTS.cooldownMinutes,
      storedDeviceRecovery: null
    };
  }
  return {
    deviceRecoveryOn: dto.action === 'reboot-instance',
    recoveryAfterMinutes: msToMinutes(dto.afterMs),
    recoveryMaxAttempts: dto.maxAttempts,
    recoveryCooldownMinutes: msToMinutes(dto.cooldownMs),
    storedDeviceRecovery: dto
  };
};

/** Checks the form fields. It checks only when the option is on. The limits are the limits of the service. */
export const recoveryFieldErrors = (
  value: Pick<RecoveryFormFields, 'deviceRecoveryOn' | 'recoveryAfterMinutes' | 'recoveryMaxAttempts' | 'recoveryCooldownMinutes'>,
  emulatorInstanceName: string
): RecoveryFieldErrors => {
  const errors: RecoveryFieldErrors = {};
  if (!value.deviceRecoveryOn) return errors;
  if (!emulatorInstanceName.trim()) errors.instanceName = RECOVERY_INSTANCE_ERROR;
  const afterMs = minutesToMs(value.recoveryAfterMinutes);
  if (!Number.isFinite(afterMs) || afterMs < MIN_AFTER_MS || afterMs > MAX_INT32) errors.afterMinutes = RECOVERY_AFTER_ERROR;
  const attempts = value.recoveryMaxAttempts;
  if (!Number.isInteger(attempts) || attempts < MIN_ATTEMPTS || attempts > MAX_ATTEMPTS) {
    errors.maxAttempts = RECOVERY_ATTEMPTS_ERROR;
  }
  const cooldownMs = minutesToMs(value.recoveryCooldownMinutes);
  if (!Number.isFinite(cooldownMs) || cooldownMs < 0 || cooldownMs > MAX_INT32) {
    errors.cooldownMinutes = RECOVERY_COOLDOWN_ERROR;
  }
  return errors;
};

/** True when the result of <see cref="recoveryFieldErrors"/> has at least one message. */
export const hasRecoveryErrors = (errors: RecoveryFieldErrors): boolean => Object.keys(errors).length > 0;

/**
 * Gives the milliseconds to send for a minute field. A field that the operator did not change keeps the stored
 * milliseconds, so that a rounded display value (for example 90500 ms shown as 1.5) does not change the stored value.
 */
const msToSend = (minutes: number, storedMs: number | undefined): number =>
  storedMs !== undefined && msToMinutes(storedMs) === minutes ? storedMs : minutesToMs(minutes);

/**
 * Gives the <c>deviceRecovery</c> member to send, or undefined when the request must have no member.
 * Option on: the settings with the action reboot-instance. Option off and the queue had settings: the settings
 * with the action none. Option off and the queue had none: no member, and the service keeps the null value.
 */
export const recoveryToSend = (value: RecoveryFormFields): QueueDeviceRecoveryDto | undefined => {
  const stored = value.storedDeviceRecovery;
  if (!value.deviceRecoveryOn && !stored) return undefined;
  return {
    action: value.deviceRecoveryOn ? 'reboot-instance' : 'none',
    afterMs: msToSend(value.recoveryAfterMinutes, stored?.afterMs),
    maxAttempts: value.recoveryMaxAttempts,
    cooldownMs: msToSend(value.recoveryCooldownMinutes, stored?.cooldownMs)
  };
};

import {
  RECOVERY_DEFAULTS,
  hasRecoveryErrors,
  msToMinutes,
  recoveryFieldErrors,
  recoveryFormFromDto,
  recoveryToSend
} from '../deviceRecoveryForm';
import type { QueueDeviceRecoveryDto } from '../../../services/queues';

const reboot: QueueDeviceRecoveryDto = { action: 'reboot-instance', afterMs: 120000, maxAttempts: 3, cooldownMs: 30000 };
const none: QueueDeviceRecoveryDto = { action: 'none', afterMs: 300000, maxAttempts: 2, cooldownMs: 180000 };

const valid = { deviceRecoveryOn: true, recoveryAfterMinutes: 5, recoveryMaxAttempts: 2, recoveryCooldownMinutes: 3 };

describe('deviceRecoveryForm (feature 199)', () => {
  it('has the defaults of a new queue', () => {
    expect(RECOVERY_DEFAULTS).toEqual({ on: false, afterMinutes: 5, maxAttempts: 2, cooldownMinutes: 3 });
  });

  describe('msToMinutes', () => {
    it('rounds to one decimal place', () => {
      expect(msToMinutes(90500)).toBe(1.5);
      expect(msToMinutes(180000)).toBe(3);
      expect(msToMinutes(0)).toBe(0);
    });
  });

  describe('recoveryFormFromDto', () => {
    it('gives the defaults for null and undefined', () => {
      for (const dto of [null, undefined]) {
        expect(recoveryFormFromDto(dto)).toEqual({
          deviceRecoveryOn: false,
          recoveryAfterMinutes: 5,
          recoveryMaxAttempts: 2,
          recoveryCooldownMinutes: 3,
          storedDeviceRecovery: null
        });
      }
    });

    it('gives the option off for the action none and keeps the stored values', () => {
      const form = recoveryFormFromDto({ action: 'none', afterMs: 600000, maxAttempts: 4, cooldownMs: 0 });
      expect(form.deviceRecoveryOn).toBe(false);
      expect(form.recoveryAfterMinutes).toBe(10);
      expect(form.recoveryMaxAttempts).toBe(4);
      expect(form.recoveryCooldownMinutes).toBe(0);
      expect(form.storedDeviceRecovery).toEqual({ action: 'none', afterMs: 600000, maxAttempts: 4, cooldownMs: 0 });
    });

    it('gives the option on for reboot-instance', () => {
      const form = recoveryFormFromDto(reboot);
      expect(form.deviceRecoveryOn).toBe(true);
      expect(form.recoveryAfterMinutes).toBe(2);
      expect(form.recoveryCooldownMinutes).toBe(0.5);
      expect(form.storedDeviceRecovery).toBe(reboot);
    });
  });

  describe('recoveryToSend', () => {
    it('on, no stored settings: sends reboot-instance', () => {
      const form = { ...recoveryFormFromDto(null), deviceRecoveryOn: true };
      expect(recoveryToSend(form)).toEqual({ action: 'reboot-instance', afterMs: 300000, maxAttempts: 2, cooldownMs: 180000 });
    });

    it('on, stored settings: sends reboot-instance', () => {
      const form = recoveryFormFromDto(none);
      expect(recoveryToSend({ ...form, deviceRecoveryOn: true })).toEqual({ ...none, action: 'reboot-instance' });
    });

    it('off, stored settings: sends the action none with the values', () => {
      const form = { ...recoveryFormFromDto(reboot), deviceRecoveryOn: false };
      expect(recoveryToSend(form)).toEqual({ ...reboot, action: 'none' });
    });

    it('off, no stored settings: sends no member', () => {
      expect(recoveryToSend(recoveryFormFromDto(null))).toBeUndefined();
    });

    it('an unchanged field that holds 90500 ms sends 90500', () => {
      const stored: QueueDeviceRecoveryDto = { action: 'reboot-instance', afterMs: 90500, maxAttempts: 2, cooldownMs: 90500 };
      const sent = recoveryToSend(recoveryFormFromDto(stored))!;
      expect(sent.afterMs).toBe(90500);
      expect(sent.cooldownMs).toBe(90500);
    });

    it('a changed field sends the rounded milliseconds', () => {
      const stored: QueueDeviceRecoveryDto = { action: 'reboot-instance', afterMs: 90500, maxAttempts: 2, cooldownMs: 90500 };
      const form = { ...recoveryFormFromDto(stored), recoveryAfterMinutes: 2.5, recoveryCooldownMinutes: 0.1 };
      const sent = recoveryToSend(form)!;
      expect(sent.afterMs).toBe(150000);
      expect(sent.cooldownMs).toBe(6000);
    });

    it('sends the attempts as typed', () => {
      const form = { ...recoveryFormFromDto(null), deviceRecoveryOn: true, recoveryMaxAttempts: 4 };
      expect(recoveryToSend(form)!.maxAttempts).toBe(4);
    });
  });

  describe('recoveryFieldErrors', () => {
    it('has no errors when the option is off, also for bad values', () => {
      const errors = recoveryFieldErrors(
        { deviceRecoveryOn: false, recoveryAfterMinutes: 0, recoveryMaxAttempts: 9, recoveryCooldownMinutes: -1 },
        ''
      );
      expect(errors).toEqual({});
      expect(hasRecoveryErrors(errors)).toBe(false);
    });

    it('has no errors for valid values', () => {
      expect(recoveryFieldErrors(valid, 'LDPlayer-1')).toEqual({});
    });

    it('needs an instance name when the option is on', () => {
      expect(recoveryFieldErrors(valid, '  ').instanceName).toBe('Device recovery needs an emulator instance name.');
    });

    it('needs a wait of 1 minute or more, and a wait that fits a 32-bit integer', () => {
      expect(recoveryFieldErrors({ ...valid, recoveryAfterMinutes: 0.5 }, 'A').afterMinutes).toBe('Enter 1 minute or more.');
      expect(recoveryFieldErrors({ ...valid, recoveryAfterMinutes: Number.NaN }, 'A').afterMinutes).toBe('Enter 1 minute or more.');
      expect(recoveryFieldErrors({ ...valid, recoveryAfterMinutes: 100000 }, 'A').afterMinutes).toBe('Enter 1 minute or more.');
      expect(recoveryFieldErrors({ ...valid, recoveryAfterMinutes: 1 }, 'A').afterMinutes).toBeUndefined();
    });

    it('needs a whole number of attempts from 1 to 5', () => {
      for (const attempts of [0, 6, 1.5, Number.NaN]) {
        expect(recoveryFieldErrors({ ...valid, recoveryMaxAttempts: attempts }, 'A').maxAttempts).toBe('Enter a whole number from 1 to 5.');
      }
      for (const attempts of [1, 5]) {
        expect(recoveryFieldErrors({ ...valid, recoveryMaxAttempts: attempts }, 'A').maxAttempts).toBeUndefined();
      }
    });

    it('needs a time between attempts of 0 minutes or more', () => {
      expect(recoveryFieldErrors({ ...valid, recoveryCooldownMinutes: -1 }, 'A').cooldownMinutes).toBe('Enter 0 minutes or more.');
      expect(recoveryFieldErrors({ ...valid, recoveryCooldownMinutes: 100000 }, 'A').cooldownMinutes).toBe('Enter 0 minutes or more.');
      expect(recoveryFieldErrors({ ...valid, recoveryCooldownMinutes: 0 }, 'A').cooldownMinutes).toBeUndefined();
    });
  });
});

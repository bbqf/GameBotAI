import {
  EDITOR_PATHS,
  IMAGE_HINT,
  NUMERIC_TEXT_HINT,
  PARAMETRIZABLE_PATHS,
  dropForeignTemplateKeys,
  isImagePath,
  isParametrizablePath,
  parameterFit,
  templateParameterName,
} from '../fieldTemplatePaths';
import type { ParameterDeclaration } from '../types';

const decl = (extra: Partial<ParameterDeclaration>): ParameterDeclaration => ({
  name: 'p',
  type: 'text',
  required: false,
  ...extra,
});

describe('fieldTemplatePaths', () => {
  it('lists 16 paths and 12 editor paths', () => {
    expect(PARAMETRIZABLE_PATHS).toHaveLength(16);
    expect(EDITOR_PATHS).toHaveLength(12);
    expect(EDITOR_PATHS.some((p) => p.startsWith('ensureGameRunning.'))).toBe(false);
  });

  it('knows image and parametrizable paths', () => {
    expect(isImagePath('primitiveTap.detectionTarget.referenceImageId')).toBe(true);
    expect(isImagePath('swipe.startX')).toBe(false);
    expect(isParametrizablePath('swipe.startX')).toBe(true);
    expect(isParametrizablePath('swipe.holdMs')).toBe(false);
  });

  describe('parameterFit', () => {
    it('fits a number parameter in a numeric field', () => {
      expect(parameterFit(decl({ type: 'number' }), 'swipe.durationMs')).toEqual({ fits: true });
      expect(parameterFit(decl({ type: 'number', kind: 'number' }), 'swipe.durationMs')).toEqual({ fits: true });
    });

    it('shows the hint for a text parameter, a pattern, and an image kind in a numeric field', () => {
      expect(parameterFit(decl({ type: 'text' }), 'swipe.durationMs')).toEqual({ fits: false, hint: NUMERIC_TEXT_HINT });
      expect(parameterFit(decl({ type: 'number', pattern: 'x{value}' }), 'swipe.durationMs')).toEqual({ fits: false, hint: NUMERIC_TEXT_HINT });
      expect(parameterFit(decl({ type: 'text', kind: 'image' }), 'swipe.durationMs')).toEqual({ fits: false, hint: NUMERIC_TEXT_HINT });
    });

    it('fits a choice by the type of its options', () => {
      expect(parameterFit(decl({ type: 'number', kind: 'choice' }), 'swipe.durationMs')).toEqual({ fits: true });
      expect(parameterFit(decl({ type: 'text', kind: 'choice' }), 'swipe.durationMs')).toEqual({ fits: false, hint: NUMERIC_TEXT_HINT });
    });

    it('fits only the image picker kind in an image field', () => {
      const path = 'primitiveTap.detectionTarget.referenceImageId';
      expect(parameterFit(decl({ kind: 'image' }), path)).toEqual({ fits: true });
      expect(parameterFit(decl({ type: 'text' }), path)).toEqual({ fits: false, hint: IMAGE_HINT });
      expect(parameterFit(decl({ type: 'number' }), path)).toEqual({ fits: false, hint: IMAGE_HINT });
      expect(parameterFit(decl({ kind: 'choice' }), path)).toEqual({ fits: false, hint: IMAGE_HINT });
    });
  });

  describe('templateParameterName', () => {
    it('reads a whole placeholder', () => {
      expect(templateParameterName('{{delay}}')).toBe('delay');
      expect(templateParameterName('{{ queue.gameId }}')).toBe('queue.gameId');
    });
    it('gives undefined for a partial placeholder or plain text', () => {
      expect(templateParameterName('x{{delay}}')).toBeUndefined();
      expect(templateParameterName('delay')).toBeUndefined();
      expect(templateParameterName(undefined)).toBeUndefined();
    });
  });

  describe('dropForeignTemplateKeys', () => {
    const templates = {
      'swipe.startX': '{{a}}',
      'primitiveTap.detectionTarget.offsetX': '{{b}}',
      'ensureGameRunning.readinessTimeoutMs': '{{c}}',
    };

    it('drops keys of other step types and keeps all other keys', () => {
      expect(dropForeignTemplateKeys(templates, 'PrimitiveTap')).toEqual({
        'primitiveTap.detectionTarget.offsetX': '{{b}}',
      });
      expect(dropForeignTemplateKeys({ ...templates, 'custom.key': '{{d}}' }, 'Swipe')).toEqual({
        'swipe.startX': '{{a}}',
        'custom.key': '{{d}}',
      });
    });

    it('keeps all keys when the type is the same', () => {
      const same = { 'swipe.startX': '{{a}}', 'swipe.durationMs': '{{b}}' };
      expect(dropForeignTemplateKeys(same, 'Swipe')).toEqual(same);
    });

    it('gives undefined when no key is left', () => {
      expect(dropForeignTemplateKeys({ 'swipe.startX': '{{a}}' }, 'KeyInput')).toBeUndefined();
      expect(dropForeignTemplateKeys(undefined, 'Swipe')).toBeUndefined();
    });
  });
});

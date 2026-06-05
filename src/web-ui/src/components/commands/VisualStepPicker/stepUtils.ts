import type { RecordedStep } from '../../../types/picker';
import type { CommandStepDto } from '../../../services/commands';

export function toCommandStepDto(step: RecordedStep): CommandStepDto {
  switch (step.type) {
    case 'PrimitiveTap':
      return {
        type: 'PrimitiveTap',
        order: 0,
        primitiveTap: {
          detectionTarget: {
            referenceImageId: step.imageId,
            offsetX: step.offsetX,
            offsetY: step.offsetY,
          },
        },
      };
    case 'KeyInput':
      return { type: 'KeyInput', order: 0, keyInput: { key: step.key } };
    case 'Swipe':
      return {
        type: 'Swipe',
        order: 0,
        swipe: {
          startX: step.startX,
          startY: step.startY,
          endX: step.endX,
          endY: step.endY,
          durationMs: step.durationMs,
        },
      };
  }
}

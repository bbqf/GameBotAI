import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { PrimitiveActionFields } from '../PrimitiveActionFields';
import {
  QuizActionFields,
  QUIZ_LEARN_TYPE,
  QUIZ_LOOKUP_TYPE,
  createDefaultQuizAction,
  isQuizActionType,
} from '../QuizActionFields';
import type { SequencePrimitiveActionPayload } from '../../../types/sequenceFlow';
import { validatePerStepConditions } from '../../../lib/validation';

const lookupAction = (): SequencePrimitiveActionPayload => ({
  type: QUIZ_LOOKUP_TYPE,
  schemaVersion: '1',
  payload: {
    game: 'pns',
    category: 'radio',
    store: 'pns-quiz',
    question: { x: 40, y: 200, width: 400, height: 90 },
    answers: [
      { x: 40, y: 320, width: 400, height: 60 },
      { x: 40, y: 400, width: 400, height: 60 },
      { x: 40, y: 480, width: 400, height: 60 },
    ],
    similarity: 0.85,
  },
});

const learnAction = (): SequencePrimitiveActionPayload => ({
  type: QUIZ_LEARN_TYPE,
  schemaVersion: '1',
  payload: { lookupStepId: 'lookup-1', correctPosition: 2 },
});

const renderFields = (action: SequencePrimitiveActionPayload, onChange = jest.fn()) => {
  render(<QuizActionFields action={action} onChange={onChange} scope={[]} idPrefix="q" />);
  return onChange;
};

describe('createDefaultQuizAction', () => {
  it('gives a lookup with two answer regions and the default similarity', () => {
    const action = createDefaultQuizAction(QUIZ_LOOKUP_TYPE);
    expect(action.type).toBe('quiz-lookup');
    expect((action.payload.answers as unknown[]).length).toBe(2);
    expect(action.payload.similarity).toBe(0.85);
  });

  it('gives a learn that reads by position 1', () => {
    const action = createDefaultQuizAction(QUIZ_LEARN_TYPE);
    expect(action.type).toBe('quiz-learn');
    expect(action.payload.correctPosition).toBe(1);
    expect(action.payload.lookupStepId).toBe('');
  });

  it('knows the two quiz types', () => {
    expect(isQuizActionType('quiz-lookup')).toBe(true);
    expect(isQuizActionType('quiz-learn')).toBe(true);
    expect(isQuizActionType('tap')).toBe(false);
  });
});

describe('QuizActionFields: quiz-lookup', () => {
  it('shows the game, category, store, and similarity', () => {
    renderFields(lookupAction());
    expect(screen.getByLabelText('game')).toHaveValue('pns');
    expect(screen.getByLabelText('category')).toHaveValue('radio');
    expect(screen.getByLabelText('store')).toHaveValue('pns-quiz');
    expect(screen.getByLabelText('similarity')).toHaveValue('0.85');
  });

  it('writes a changed text field back to the payload', () => {
    const onChange = renderFields(lookupAction());
    fireEvent.change(screen.getByLabelText('category'), { target: { value: 'history' } });
    const next = onChange.mock.calls[0][0] as SequencePrimitiveActionPayload;
    expect(next.payload.category).toBe('history');
    expect(next.payload.game).toBe('pns');
    expect((next.payload.answers as unknown[]).length).toBe(3);
  });

  it('keeps the similarity a number, and removes it when it is empty', () => {
    const onChange = renderFields(lookupAction());
    fireEvent.change(screen.getByLabelText('similarity'), { target: { value: '0.9' } });
    expect((onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.similarity).toBe(0.9);
    fireEvent.change(screen.getByLabelText('similarity'), { target: { value: '' } });
    expect('similarity' in (onChange.mock.calls[1][0] as SequencePrimitiveActionPayload).payload).toBe(false);
  });

  it('edits the question region as numbers', () => {
    const onChange = renderFields(lookupAction());
    expect(screen.getByLabelText('width', { selector: '#q-question-width' })).toHaveValue(400);
    fireEvent.change(document.getElementById('q-question-y') as HTMLInputElement, { target: { value: '250' } });
    const next = onChange.mock.calls[0][0] as SequencePrimitiveActionPayload;
    expect(next.payload.question).toEqual({ x: 40, y: 250, width: 400, height: 90 });
  });

  it('treats a text that is not a number as 0 in a region', () => {
    const onChange = renderFields(lookupAction());
    fireEvent.change(document.getElementById('q-question-x') as HTMLInputElement, { target: { value: '' } });
    const next = onChange.mock.calls[0][0] as SequencePrimitiveActionPayload;
    expect((next.payload.question as { x: number }).x).toBe(0);
  });

  it('edits one answer region', () => {
    const onChange = renderFields(lookupAction());
    fireEvent.change(document.getElementById('q-answer-2-y') as HTMLInputElement, { target: { value: '410' } });
    const answers = (onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.answers as { y: number }[];
    expect(answers.map((a) => a.y)).toEqual([320, 410, 480]);
  });

  it('adds an answer region', () => {
    const onChange = renderFields(lookupAction());
    fireEvent.click(screen.getByTestId('quiz-add-answer'));
    const answers = (onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.answers as unknown[];
    expect(answers).toHaveLength(4);
  });

  it('removes an answer region, but not below two', () => {
    const onChange = renderFields(lookupAction());
    fireEvent.click(screen.getByTestId('quiz-remove-answer-1'));
    const answers = (onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.answers as { y: number }[];
    expect(answers.map((a) => a.y)).toEqual([400, 480]);
  });

  it('disables remove when only two answers remain', () => {
    const action = lookupAction();
    (action.payload.answers as unknown[]).pop();
    renderFields(action);
    expect(screen.getByTestId('quiz-remove-answer-1')).toBeDisabled();
  });

  it('shows missing values as empty fields and zero regions', () => {
    renderFields({ type: QUIZ_LOOKUP_TYPE, payload: {} });
    expect(screen.getByLabelText('game')).toHaveValue('');
    expect(document.getElementById('q-question-width')).toHaveValue(0);
  });
});

describe('QuizActionFields: quiz-learn', () => {
  it('shows the lookup step id and the position', () => {
    renderFields(learnAction());
    expect(screen.getByLabelText('lookupStepId')).toHaveValue('lookup-1');
    expect(screen.getByLabelText('correctPosition')).toHaveValue(2);
    expect(screen.getByTestId('quiz-learn-mode')).toHaveValue('position');
  });

  it('writes a changed position back, never below 1', () => {
    const onChange = renderFields(learnAction());
    fireEvent.change(screen.getByLabelText('correctPosition'), { target: { value: '3' } });
    expect((onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.correctPosition).toBe(3);
    fireEvent.change(screen.getByLabelText('correctPosition'), { target: { value: '0' } });
    expect((onChange.mock.calls[1][0] as SequencePrimitiveActionPayload).payload.correctPosition).toBe(1);
  });

  it('switches to a region, and keeps exactly one of region and position', () => {
    const onChange = renderFields(learnAction());
    fireEvent.change(screen.getByTestId('quiz-learn-mode'), { target: { value: 'region' } });
    const next = onChange.mock.calls[0][0] as SequencePrimitiveActionPayload;
    expect('correctPosition' in next.payload).toBe(false);
    expect(next.payload.correctRegion).toBeDefined();
    expect(next.payload.lookupStepId).toBe('lookup-1');
  });

  it('shows and edits a correct region', () => {
    const action: SequencePrimitiveActionPayload = {
      type: QUIZ_LEARN_TYPE,
      payload: { lookupStepId: 'lookup-1', correctRegion: { x: 1, y: 2, width: 3, height: 4 } },
    };
    const onChange = renderFields(action);
    expect(screen.getByTestId('quiz-learn-mode')).toHaveValue('region');
    fireEvent.change(document.getElementById('q-correctRegion-height') as HTMLInputElement, { target: { value: '9' } });
    const region = (onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.correctRegion;
    expect(region).toEqual({ x: 1, y: 2, width: 3, height: 9 });
  });

  it('switches back to a position', () => {
    const action: SequencePrimitiveActionPayload = {
      type: QUIZ_LEARN_TYPE,
      payload: { lookupStepId: 'lookup-1', correctRegion: { x: 1, y: 2, width: 3, height: 4 } },
    };
    const onChange = renderFields(action);
    fireEvent.change(screen.getByTestId('quiz-learn-mode'), { target: { value: 'position' } });
    const next = onChange.mock.calls[0][0] as SequencePrimitiveActionPayload;
    expect('correctRegion' in next.payload).toBe(false);
    expect(next.payload.correctPosition).toBe(1);
  });

  it('writes a changed lookup step id back', () => {
    const onChange = renderFields(learnAction());
    fireEvent.change(screen.getByLabelText('lookupStepId'), { target: { value: 'lookup-2' } });
    expect((onChange.mock.calls[0][0] as SequencePrimitiveActionPayload).payload.lookupStepId).toBe('lookup-2');
  });
});

describe('PrimitiveActionFields routing', () => {
  it('uses the quiz form for a quiz type', () => {
    render(<PrimitiveActionFields action={lookupAction()} onChange={jest.fn()} scope={[]} idPrefix="p" />);
    expect(screen.getByTestId('quiz-action-fields')).toBeInTheDocument();
  });

  it('uses the generic form for another type', () => {
    render(
      <PrimitiveActionFields
        action={{ type: 'tap', payload: { x: 1, y: 2 } }}
        onChange={jest.fn()}
        scope={[]}
        idPrefix="p"
      />,
    );
    expect(screen.getByTestId('primitive-action-fields')).toBeInTheDocument();
    expect(screen.queryByTestId('quiz-action-fields')).not.toBeInTheDocument();
  });
});

describe('commandOutcome condition on a quiz outcome', () => {
  const stepsWith = (expectedState: string) => [
    {
      stepId: 'lookup-1',
      stepType: 'Action' as const,
      primitiveAction: lookupAction(),
    },
    {
      stepId: 'tap-2',
      stepType: 'Action' as const,
      primitiveAction: { type: 'tap', payload: { x: 1, y: 2 } },
      condition: { type: 'commandOutcome' as const, stepRef: 'lookup-1', expectedState },
    },
  ];

  it.each(['pick_1', 'pick_12', 'unreadable', 'service_unavailable', 'stored', 'skipped_no_match', 'skipped_ambiguous'])(
    'accepts %s',
    (state) => {
      expect(validatePerStepConditions(stepsWith(state) as never)).toEqual([]);
    },
  );

  it.each(['pick_0', 'pick_', 'maybe'])('rejects %s', (state) => {
    expect(validatePerStepConditions(stepsWith(state) as never)).toHaveLength(1);
  });
});

import React from 'react';
import type { SequencePrimitiveActionPayload } from '../../types/sequenceFlow';
import type { ParameterScopeEntry } from '../parameters/types';
import { ParameterizableField } from '../parameters/ParameterizableField';

/** The two quiz step types (feature 142). */
export const QUIZ_LOOKUP_TYPE = 'quiz-lookup';
export const QUIZ_LEARN_TYPE = 'quiz-learn';

export const isQuizActionType = (type: string): boolean =>
  type === QUIZ_LOOKUP_TYPE || type === QUIZ_LEARN_TYPE;

export type QuizRegion = { x: number; y: number; width: number; height: number };

const emptyRegion = (): QuizRegion => ({ x: 0, y: 0, width: 100, height: 40 });

/** The payload of a new quiz step. The author fills in the text fields and the regions. */
export const createDefaultQuizAction = (type: string): SequencePrimitiveActionPayload => {
  if (type === QUIZ_LEARN_TYPE) {
    return { type, schemaVersion: '1', payload: { lookupStepId: '', correctPosition: 1 } };
  }
  return {
    type: QUIZ_LOOKUP_TYPE,
    schemaVersion: '1',
    payload: {
      game: '',
      category: '',
      store: '',
      question: emptyRegion(),
      answers: [emptyRegion(), emptyRegion()],
      similarity: 0.85,
    },
  };
};

const asRegion = (value: unknown): QuizRegion => {
  const source = (value && typeof value === 'object' ? value : {}) as Record<string, unknown>;
  const num = (key: string): number => {
    const raw = source[key];
    return typeof raw === 'number' && Number.isFinite(raw) ? raw : 0;
  };
  return { x: num('x'), y: num('y'), width: num('width'), height: num('height') };
};

const asRegionList = (value: unknown): QuizRegion[] => (Array.isArray(value) ? value.map(asRegion) : []);

const asText = (value: unknown): string => (typeof value === 'string' ? value : value == null ? '' : String(value));

const toInt = (raw: string): number => {
  const parsed = parseInt(raw, 10);
  return Number.isFinite(parsed) ? parsed : 0;
};

type RegionInputsProps = {
  idPrefix: string;
  label: string;
  region: QuizRegion;
  disabled?: boolean;
  onChange: (region: QuizRegion) => void;
};

const REGION_KEYS: (keyof QuizRegion)[] = ['x', 'y', 'width', 'height'];

const RegionInputs: React.FC<RegionInputsProps> = ({ idPrefix, label, region, disabled, onChange }) => (
  <fieldset className="quiz-action-fields__region" data-testid={`${idPrefix}-region`}>
    <legend>{label}</legend>
    {REGION_KEYS.map((key) => (
      <label key={key} htmlFor={`${idPrefix}-${key}`}>
        {key}
        <input
          id={`${idPrefix}-${key}`}
          type="number"
          value={region[key]}
          disabled={disabled}
          onChange={(event) => onChange({ ...region, [key]: toInt(event.target.value) })}
        />
      </label>
    ))}
  </fieldset>
);

export type QuizActionFieldsProps = {
  action: SequencePrimitiveActionPayload;
  onChange: (action: SequencePrimitiveActionPayload) => void;
  scope: ParameterScopeEntry[];
  disabled?: boolean;
  idPrefix: string;
};

/**
 * Basic field form of the quiz steps (feature 142). A quiz-lookup step has a game, a category, a store, a
 * similarity limit, a question region, and two or more answer regions. A quiz-learn step has the id of an
 * earlier lookup step and the correct answer, given by position or by region. The text fields take a
 * parameter reference, like the other inline actions.
 */
export const QuizActionFields: React.FC<QuizActionFieldsProps> = ({ action, onChange, scope, disabled, idPrefix }) => {
  const payload = action.payload ?? {};
  const set = (next: Record<string, unknown>) => onChange({ ...action, payload: next });
  const setKey = (key: string, value: unknown) => set({ ...payload, [key]: value });

  if (action.type === QUIZ_LEARN_TYPE) {
    const byRegion = payload.correctRegion !== undefined && payload.correctPosition === undefined;
    return (
      <div className="quiz-action-fields" data-testid="quiz-action-fields">
        <p className="primitive-action-fields__type">
          Inline action: <code data-testid="primitive-action-type">{action.type}</code>
        </p>
        <ParameterizableField
          id={`${idPrefix}-lookupStepId`}
          label="lookupStepId"
          value={asText(payload.lookupStepId)}
          scope={scope}
          disabled={disabled}
          onChange={(next) => setKey('lookupStepId', next)}
        />
        <label htmlFor={`${idPrefix}-mode`}>
          Correct answer by
          <select
            id={`${idPrefix}-mode`}
            data-testid="quiz-learn-mode"
            value={byRegion ? 'region' : 'position'}
            disabled={disabled}
            onChange={(event) => {
              const { correctPosition: _position, correctRegion: _region, ...rest } = payload;
              void _position;
              void _region;
              set(event.target.value === 'region'
                ? { ...rest, correctRegion: emptyRegion() }
                : { ...rest, correctPosition: 1 });
            }}
          >
            <option value="position">position</option>
            <option value="region">region</option>
          </select>
        </label>
        {byRegion ? (
          <RegionInputs
            idPrefix={`${idPrefix}-correctRegion`}
            label="correctRegion"
            region={asRegion(payload.correctRegion)}
            disabled={disabled}
            onChange={(region) => setKey('correctRegion', region)}
          />
        ) : (
          <label htmlFor={`${idPrefix}-correctPosition`}>
            correctPosition
            <input
              id={`${idPrefix}-correctPosition`}
              type="number"
              min={1}
              value={typeof payload.correctPosition === 'number' ? payload.correctPosition : 1}
              disabled={disabled}
              onChange={(event) => setKey('correctPosition', Math.max(1, toInt(event.target.value)))}
            />
          </label>
        )}
      </div>
    );
  }

  const answers = asRegionList(payload.answers);
  const setAnswers = (next: QuizRegion[]) => setKey('answers', next);

  return (
    <div className="quiz-action-fields" data-testid="quiz-action-fields">
      <p className="primitive-action-fields__type">
        Inline action: <code data-testid="primitive-action-type">{action.type}</code>
      </p>
      {(['game', 'category', 'store'] as const).map((name) => (
        <ParameterizableField
          key={name}
          id={`${idPrefix}-${name}`}
          label={name}
          value={asText(payload[name])}
          scope={scope}
          disabled={disabled}
          onChange={(next) => setKey(name, next)}
        />
      ))}
      <ParameterizableField
        id={`${idPrefix}-similarity`}
        label="similarity"
        value={asText(payload.similarity)}
        numeric
        scope={scope}
        disabled={disabled}
        onChange={(next) => {
          const { similarity: _similarity, ...rest } = payload;
          void _similarity;
          const trimmed = next.trim();
          if (trimmed === '') {
            set(rest);
          } else {
            const parsed = Number(trimmed);
            setKey('similarity', Number.isFinite(parsed) ? parsed : next);
          }
        }}
      />
      <RegionInputs
        idPrefix={`${idPrefix}-question`}
        label="question"
        region={asRegion(payload.question)}
        disabled={disabled}
        onChange={(region) => setKey('question', region)}
      />
      {answers.map((region, index) => (
        <div key={index} className="quiz-action-fields__answer">
          <RegionInputs
            idPrefix={`${idPrefix}-answer-${index + 1}`}
            label={`answer ${index + 1}`}
            region={region}
            disabled={disabled}
            onChange={(next) => setAnswers(answers.map((candidate, i) => (i === index ? next : candidate)))}
          />
          <button
            type="button"
            data-testid={`quiz-remove-answer-${index + 1}`}
            disabled={disabled || answers.length <= 2}
            onClick={() => setAnswers(answers.filter((_, i) => i !== index))}
          >
            Remove answer {index + 1}
          </button>
        </div>
      ))}
      <button
        type="button"
        data-testid="quiz-add-answer"
        disabled={disabled}
        onClick={() => setAnswers([...answers, emptyRegion()])}
      >
        Add answer region
      </button>
    </div>
  );
};

import type { QueueTemplateDetail, SaveQueueTemplate } from '../services/queueTemplates';
import type { ParameterBinding } from '../components/parameters/types';
import { toSaveEntry } from './queueEntryToggle';

/**
 * Builds the save request that sets the parameter values of one template entry (feature 185, research R-7).
 * Every entry is sent in the same order. Only the values of the entry at `index` change.
 */
export const setTemplateEntryParameters = (
  template: QueueTemplateDetail,
  index: number,
  values: ParameterBinding[]
): SaveQueueTemplate => ({
  name: template.name,
  overwrite: true,
  entries: template.entries.map((entry, i) =>
    toSaveEntry(i === index ? { ...entry, parameterValues: values } : entry, entry.enabled ?? true))
});

export type ThemeTokens = {
  colors: {
    text: string;
    muted: string;
    background: string;
    surface: string;
    surfaceAlt: string;
    surfaceStrong: string;
    border: string;
    borderStrong: string;
    primary: string;
    primarySoft: string;
    accent: string;
    focus: string;
    danger: string;
    success: string;
    warning: string;
    ambientSpot: string;
  };
  spacing: {
    xs: string;
    sm: string;
    md: string;
    lg: string;
    xl: string;
    xxl: string;
  };
  radii: {
    sm: string;
    md: string;
    lg: string;
    xl: string;
    pill: string;
  };
  typography: {
    fontFamily: string;
    baseSize: string;
    labelSize: string;
    lineHeight: string;
    letterTight: string;
  };
  button: {
    height: string;
    paddingX: string;
    paddingY: string;
    fontSize: string;
    fontWeight: number;
    radius: string;
    gap: string;
  };
  shadows: {
    card: string;
    raised: string;
  };
  status: {
    running: StatusSwatch;
    pending: StatusSwatch;
    succeeded: StatusSwatch;
    failed: StatusSwatch;
    stopping: StatusSwatch;
    unknown: StatusSwatch;
  };
};

export type StatusSwatch = {
  bg: string;
  border: string;
  text: string;
};

export const tokens: ThemeTokens = {
  colors: {
    text: '#0f172a',
    muted: '#475467',
    background: '#f5f7fb',
    surface: '#ffffff',
    surfaceAlt: '#f8fafc',
    surfaceStrong: '#eef2ff',
    border: '#dfe5f2',
    borderStrong: '#cbd5e1',
    primary: '#2563eb',
    primarySoft: '#e4edff',
    accent: '#0ea5e9',
    focus: '#fbbf24',
    danger: '#b91c1c',
    success: '#15803d',
    warning: '#b45309',
    ambientSpot:
      'radial-gradient(32% 32% at 18% 12%, rgba(59, 130, 246, 0.08) 0%, rgba(59, 130, 246, 0) 100%), radial-gradient(24% 26% at 82% 18%, rgba(16, 185, 129, 0.1) 0%, rgba(16, 185, 129, 0) 100%)'
  },
  spacing: {
    xs: '4px',
    sm: '8px',
    md: '12px',
    lg: '16px',
    xl: '24px',
    xxl: '32px'
  },
  radii: {
    sm: '6px',
    md: '10px',
    lg: '14px',
    xl: '18px',
    pill: '999px'
  },
  typography: {
    fontFamily: "'Space Grotesk','Segoe UI Variable','Segoe UI',system-ui,sans-serif",
    baseSize: '16px',
    labelSize: '14px',
    lineHeight: '1.55',
    letterTight: '-0.01em'
  },
  button: {
    height: '44px',
    paddingX: '16px',
    paddingY: '10px',
    fontSize: '15px',
    fontWeight: 700,
    radius: '12px',
    gap: '10px'
  },
  shadows: {
    card: '0 10px 30px rgba(15, 23, 42, 0.08)',
    raised: '0 16px 40px rgba(15, 23, 42, 0.12)'
  },
  status: {
    running: { bg: '#ecfdf3', border: '#22c55e', text: '#0f5132' },
    pending: { bg: '#fefce8', border: '#facc15', text: '#854d0e' },
    succeeded: { bg: '#f0fdf4', border: '#16a34a', text: '#06603b' },
    failed: { bg: '#fef2f2', border: '#f87171', text: '#991b1b' },
    stopping: { bg: '#e0f2fe', border: '#60a5fa', text: '#1d4ed8' },
    unknown: { bg: '#e2e8f0', border: '#cbd5e1', text: '#334155' }
  }
};

const cssVarMap: Record<string, string> = {
  '--gb-font-family': tokens.typography.fontFamily,
  '--gb-font-size-base': tokens.typography.baseSize,
  '--gb-font-size-label': tokens.typography.labelSize,
  '--gb-line-height-base': tokens.typography.lineHeight,
  '--gb-letter-tight': tokens.typography.letterTight,
  '--gb-color-text': tokens.colors.text,
  '--gb-color-muted': tokens.colors.muted,
  '--gb-color-bg': tokens.colors.background,
  '--gb-color-surface': tokens.colors.surface,
  '--gb-color-surface-alt': tokens.colors.surfaceAlt,
  '--gb-color-surface-strong': tokens.colors.surfaceStrong,
  '--gb-color-border': tokens.colors.border,
  '--gb-color-border-strong': tokens.colors.borderStrong,
  '--gb-color-primary': tokens.colors.primary,
  '--gb-color-primary-soft': tokens.colors.primarySoft,
  '--gb-color-accent': tokens.colors.accent,
  '--gb-color-focus': tokens.colors.focus,
  '--gb-color-danger': tokens.colors.danger,
  '--gb-color-success': tokens.colors.success,
  '--gb-color-warning': tokens.colors.warning,
  '--gb-ambient-spot': tokens.colors.ambientSpot,
  '--gb-space-1': tokens.spacing.xs,
  '--gb-space-2': tokens.spacing.sm,
  '--gb-space-3': tokens.spacing.md,
  '--gb-space-4': tokens.spacing.lg,
  '--gb-space-5': tokens.spacing.xl,
  '--gb-space-6': tokens.spacing.xxl,
  '--gb-radius-sm': tokens.radii.sm,
  '--gb-radius-md': tokens.radii.md,
  '--gb-radius-lg': tokens.radii.lg,
  '--gb-radius-xl': tokens.radii.xl,
  '--gb-radius-pill': tokens.radii.pill,
  '--gb-button-height': tokens.button.height,
  '--gb-button-padding-x': tokens.button.paddingX,
  '--gb-button-padding-y': tokens.button.paddingY,
  '--gb-button-font-size': tokens.button.fontSize,
  '--gb-button-font-weight': `${tokens.button.fontWeight}`,
  '--gb-button-radius': tokens.button.radius,
  '--gb-button-gap': tokens.button.gap,
  '--gb-shadow-card': tokens.shadows.card,
  '--gb-shadow-raised': tokens.shadows.raised,
  '--gb-status-running-bg': tokens.status.running.bg,
  '--gb-status-running-border': tokens.status.running.border,
  '--gb-status-running-text': tokens.status.running.text,
  '--gb-status-pending-bg': tokens.status.pending.bg,
  '--gb-status-pending-border': tokens.status.pending.border,
  '--gb-status-pending-text': tokens.status.pending.text,
  '--gb-status-succeeded-bg': tokens.status.succeeded.bg,
  '--gb-status-succeeded-border': tokens.status.succeeded.border,
  '--gb-status-succeeded-text': tokens.status.succeeded.text,
  '--gb-status-failed-bg': tokens.status.failed.bg,
  '--gb-status-failed-border': tokens.status.failed.border,
  '--gb-status-failed-text': tokens.status.failed.text,
  '--gb-status-stopping-bg': tokens.status.stopping.bg,
  '--gb-status-stopping-border': tokens.status.stopping.border,
  '--gb-status-stopping-text': tokens.status.stopping.text,
  '--gb-status-unknown-bg': tokens.status.unknown.bg,
  '--gb-status-unknown-border': tokens.status.unknown.border,
  '--gb-status-unknown-text': tokens.status.unknown.text
};

export const applyThemeTokens = (root: HTMLElement | null | undefined = typeof document !== 'undefined' ? document.documentElement : undefined) => {
  if (!root) return;
  for (const [name, value] of Object.entries(cssVarMap)) {
    root.style.setProperty(name, value);
  }
};

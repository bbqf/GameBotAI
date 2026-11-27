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
    dangerSoft: string;
    successSoft: string;
    warningSoft: string;
    idle: string;
    idleSoft: string;
    loop: string;
    ifBlock: string;
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
    control: string;
    dialog: string;
  };
  size: {
    appBar: string;
    control: string;
    iconButton: string;
    iconButtonTouch: string;
    icon: string;
    row: string;
    rowPhone: string;
    headerRow: string;
    keyValueRow: string;
    footer: string;
    sidePanel: string;
    formWidth: string;
    chipWidth: string;
    chipHeight: string;
    thumbnail: string;
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
    dangerSoft: '#fdecec',
    successSoft: '#e7f6ec',
    warningSoft: '#fdf1e3',
    idle: '#64748b',
    idleSoft: '#eef1f5',
    loop: '#7c3aed',
    ifBlock: '#0891b2',
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
    pill: '999px',
    control: '8px',
    dialog: '14px'
  },
  size: {
    appBar: '56px',
    control: '40px',
    iconButton: '36px',
    iconButtonTouch: '44px',
    icon: '20px',
    row: '56px',
    rowPhone: '64px',
    headerRow: '40px',
    keyValueRow: '40px',
    footer: '68px',
    sidePanel: '480px',
    formWidth: '720px',
    chipWidth: '96px',
    chipHeight: '24px',
    thumbnail: '40px'
  },
  typography: {
    fontFamily: "'Segoe UI', system-ui, -apple-system, Roboto, sans-serif",
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

export const cssVarMap: Record<string, string> = {
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
  '--gb-color-text-muted': tokens.colors.muted,
  '--gb-color-danger-soft': tokens.colors.dangerSoft,
  '--gb-color-success-soft': tokens.colors.successSoft,
  '--gb-color-warning-soft': tokens.colors.warningSoft,
  '--gb-color-idle': tokens.colors.idle,
  '--gb-color-idle-soft': tokens.colors.idleSoft,
  '--gb-color-loop': tokens.colors.loop,
  '--gb-color-if': tokens.colors.ifBlock,
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
  '--gb-radius-control': tokens.radii.control,
  '--gb-radius-dialog': tokens.radii.dialog,
  '--gb-size-app-bar': tokens.size.appBar,
  '--gb-size-control': tokens.size.control,
  '--gb-size-icon-button': tokens.size.iconButton,
  '--gb-size-icon-button-touch': tokens.size.iconButtonTouch,
  '--gb-size-icon': tokens.size.icon,
  '--gb-size-row': tokens.size.row,
  '--gb-size-row-phone': tokens.size.rowPhone,
  '--gb-size-header-row': tokens.size.headerRow,
  '--gb-size-key-value-row': tokens.size.keyValueRow,
  '--gb-size-footer': tokens.size.footer,
  '--gb-size-side-panel': tokens.size.sidePanel,
  '--gb-size-form-width': tokens.size.formWidth,
  '--gb-size-chip-width': tokens.size.chipWidth,
  '--gb-size-chip-height': tokens.size.chipHeight,
  '--gb-size-thumbnail': tokens.size.thumbnail,
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

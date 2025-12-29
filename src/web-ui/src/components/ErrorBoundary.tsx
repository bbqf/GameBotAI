import React, { Component, ErrorInfo, PropsWithChildren, ReactNode } from 'react';

type ErrorBoundaryState = { hasError: boolean; error?: Error };

export class ErrorBoundary extends Component<PropsWithChildren, ErrorBoundaryState> {
  constructor(props: PropsWithChildren) {
    super(props);
    this.state = { hasError: false };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    // eslint-disable-next-line no-console
    console.error('UI ErrorBoundary caught error', error, errorInfo);
  }

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <div role="alert" className="error-boundary">
          <h2>Something went wrong</h2>
          <p>{this.state.error?.message ?? 'Unexpected error occurred.'}</p>
          <p className="guidance">
            Try refreshing the page. If the problem persists, check your network connection and the backend API status. You can also retry the last action.
          </p>
        </div>
      );
    }
    return this.props.children ?? null;
  }
}

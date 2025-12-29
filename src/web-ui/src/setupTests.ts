import '@testing-library/jest-dom';

if (!window.matchMedia) {
	// Basic matchMedia stub for responsive hooks in tests
	// eslint-disable-next-line @typescript-eslint/ban-ts-comment
	// @ts-ignore
	window.matchMedia = (query: string) => {
		const listeners: Array<(e: MediaQueryListEvent) => void> = [];
		return {
			matches: false,
			media: query,
			onchange: null,
			addEventListener: (_: 'change', cb: (e: MediaQueryListEvent) => void) => listeners.push(cb),
			removeEventListener: (_: 'change', cb: (e: MediaQueryListEvent) => void) => {
				const idx = listeners.indexOf(cb);
				if (idx >= 0) listeners.splice(idx, 1);
			},
			dispatchEvent: () => false
		} as MediaQueryList;
	};
}
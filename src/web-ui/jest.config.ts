import type { Config } from 'jest';

const config: Config = {
  preset: 'ts-jest',
  testEnvironment: 'jsdom',
  roots: ['<rootDir>/src'],
  moduleFileExtensions: ['ts', 'tsx', 'js'],
  setupFilesAfterEnv: ['<rootDir>/src/setupTests.ts'],
  moduleNameMapper: {
    '\\.(css|less|sass|scss)$': '<rootDir>/src/testUtils/styleMock.ts'
  },
  collectCoverage: true,
  collectCoverageFrom: ['<rootDir>/src/**/*.{ts,tsx}', '!<rootDir>/src/**/*.d.ts', '!<rootDir>/src/**/__tests__/**'],
  coverageThreshold: {
    global: {
      lines: 0,
      branches: 0,
      functions: 0,
      statements: 0
    },
    './src/components/images/EmulatorCaptureCropper.tsx': {
      lines: 80,
      branches: 60,
      functions: 80,
      statements: 80
    }
  }
};

export default config;
import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
export default defineConfig({ plugins: [plugin()], base: './', server: { host: '127.0.0.1', port: 5173, strictPort: true }, build: { target: 'es2020' } });

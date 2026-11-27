// Production environment. The frontend is served by the same host as the
// API, so API calls use the relative /api path (no host baked in).
export const environment = {
  production: true,
  apiUrl: '/api',
};

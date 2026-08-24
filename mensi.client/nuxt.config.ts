export default defineNuxtConfig({
  ssr: false,
  modules: ['@pinia/nuxt'],
  css: [
    '@fontsource/montserrat/400.css',
    '@fontsource/montserrat/500.css',
    '@fontsource/montserrat/600.css',
    '@fontsource/montserrat/700.css',
    '~/assets/css/main.css',
  ],
  app: {
    head: {
      title: 'Mensi',
      htmlAttrs: { lang: 'hu' },
      meta: [{ name: 'viewport', content: 'width=device-width, initial-scale=1' }],
    },
  },
  nitro: {
    devProxy: { '/api': { target: 'http://localhost:5080/api', changeOrigin: true } },
  },
  typescript: { strict: true },
  compatibilityDate: '2026-08-24',
})

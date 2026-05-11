/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./Views/**/*.cshtml",
    // Include the @apply source so Tailwind sees the utility usages and emits
    // the corresponding component layer. Without this entry, Tailwind v3
    // tree-shakes most of Styles/app.css's @layer components{} block because
    // the .cshtml views have shrunk to a handful of pages during the SPA
    // migration; the warning "No utility classes were detected" lands
    // because no bare utility classes (bg-x, flex, grid) exist in the
    // remaining views — they all live behind @apply.
    "./Styles/**/*.css",
  ],
  theme: {
    extend: {},
  },
  plugins: [],
};

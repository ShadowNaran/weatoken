export default {
  fetch: (input, init) => fetch(input, {
    ...init,
    credentials: 'include'
  })
};

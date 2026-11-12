// The Aspire AppHost passes the API's address in this variable when it starts the dev server, so no API port
// is written down here.
const apiAddress = process.env['services__api__http__0'];

if (!apiAddress) {
  throw new Error(
    'services__api__http__0 is not set, so the dev server has no API to proxy /api to. ' +
      'Start the web app from the Aspire AppHost (aspire run in src/Nook.AppHost), which sets it.',
  );
}

module.exports = {
  '/api': {
    target: apiAddress,
  },
};

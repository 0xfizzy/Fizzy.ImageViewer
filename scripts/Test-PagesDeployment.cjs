const assert = require('node:assert/strict');
const deploy = require('./Deploy-Pages.cjs');
(async () => {
  const requests = [];
  process.env.PAGES_ARCHIVE_COMMIT = 'b'.repeat(40);
  process.env.PAGES_ARTIFACT_ID = '42';
  process.env.GITHUB_SHA = 'a'.repeat(40);
  const core = { getIDToken: async () => 'test-token', setSecret() {}, info() {} };
  const context = { repo: { owner: 'fixture', repo: 'fixture' } };
  const github = { request: async (route, args) => {
    requests.push({ route, args });
    return { data: route.startsWith('POST') ? { id: 'deployment' } : { status: 'succeed' } };
  } };
  await deploy({ github, context, core, wait: async () => {} });
  assert.equal(requests[0].args.pages_build_version, 'b'.repeat(40));
  assert.equal(requests[0].args.artifact_id, 42);
  assert.equal(requests[1].args.deployment_id, 'deployment');
  github.request = async route => ({ data: route.startsWith('POST') ? { id: 'failed' } : { status: 'deployment_failed' } });
  await assert.rejects(deploy({ github, context, core, wait: async () => {} }), /deployment_failed/);
  console.log('PASS: Pages uses archive identity and exact artifact, and propagates failed deployments.');
})().catch(error => { console.error(error); process.exitCode = 1; });

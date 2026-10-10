// GitHub Pages identifies a build by pages_build_version. Source HEAD is shared
// by development, immutable staging and activation; each has different contents.
module.exports = async ({ github, context, core, wait = ms => new Promise(resolve => setTimeout(resolve, ms)) }) => {
  const build = process.env.PAGES_ARCHIVE_COMMIT;
  const artifact = Number(process.env.PAGES_ARTIFACT_ID);
  if (!/^[a-f0-9]{40}$/.test(build) || !Number.isSafeInteger(artifact) || artifact <= 0)
    throw new Error('Invalid Pages archive commit or artifact ID.');
  const token = await core.getIDToken();
  core.setSecret(token);
  const { data } = await github.request('POST /repos/{owner}/{repo}/pages/deployments', {
    ...context.repo, artifact_id: artifact, pages_build_version: build, oidc_token: token
  });
  const deploymentId = data.id || data.status_url?.split('/').pop() || build;
  core.info(`Deploying archive ${build}, artifact ${artifact}, deployment ${deploymentId}.`);
  for (let attempt = 0; attempt < 120; attempt++) {
    await wait(5000);
    const { data: result } = await github.request('GET /repos/{owner}/{repo}/pages/deployments/{deployment_id}', {
      ...context.repo, deployment_id: deploymentId
    });
    if (result.status === 'succeed') return;
    if (['deployment_failed', 'deployment_content_failed', 'deployment_cancelled', 'deployment_lost'].includes(result.status))
      throw new Error(`Pages deployment ended with ${result.status}.`);
  }
  throw new Error('Pages deployment timed out; verify remote state before retrying.');
};

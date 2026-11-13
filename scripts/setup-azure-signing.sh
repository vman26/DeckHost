#!/usr/bin/env bash
# Sets up Azure Artifact Signing (formerly Trusted Signing) for DeckHost's Windows releases: Microsoft
# issues and renews the short-lived code-signing certificates, so there is no certificate file to buy,
# store or renew. Run it on your own computer, signed in with `az login` (and `gh auth login` to store
# the results in the repository; otherwise they are printed).
#
#   scripts/setup-azure-signing.sh <owner/repo> [identity-validation-id]
#
# Environment (optional): RESOURCE_GROUP (deckhost-signing), LOCATION (westeurope), ACCOUNT
# (deckhost-<random>), PROFILE (deckhost-public).
#
# Run it twice:
#   1. Without an identity validation id: creates the resource group and the signing account and
#      tells you to validate your identity in the Azure portal (Microsoft checks it by hand; this
#      can take days). Who qualifies: organizations in many countries; individual developers only
#      in the US and Canada (see docs/maintainers/releasing.md).
#   2. With the validation id (portal → the signing account → Identity validations): creates the
#      certificate profile and an app that may sign with it, and stores the endpoint, account,
#      profile and the app's credentials as Actions variables/secrets of <owner/repo>.
# Re-running is safe: what exists is reused (the app's secret is replaced).
set -euo pipefail

repo="${1:?usage: setup-azure-signing.sh <owner/repo> [identity-validation-id]}"
validation="${2:-}"
group="${RESOURCE_GROUP:-deckhost-signing}"
location="${LOCATION:-westeurope}"
profile="${PROFILE:-deckhost-public}"
app_name="deckhost-release-signing"

need() { command -v "$1" > /dev/null || { echo "Needs $1 ($2)." >&2; exit 1; }; }
need az "https://learn.microsoft.com/cli/azure/install-azure-cli"
az account show > /dev/null 2>&1 || { echo "Sign in first: az login" >&2; exit 1; }
az extension add --upgrade --only-show-errors --name trustedsigning > /dev/null

subscription="$(az account show --query id -o tsv)"
echo "Subscription: $(az account show --query name -o tsv) ($subscription)"

az provider register --namespace Microsoft.CodeSigning --wait > /dev/null
az group create --name "$group" --location "$location" --only-show-errors > /dev/null

# The account: reuse the one in the group, or create it (its name is global, hence the suffix).
account="${ACCOUNT:-$(az trustedsigning list --resource-group "$group" --query '[0].name' -o tsv 2> /dev/null || true)}"
if [[ -z "$account" ]]; then
  account="deckhost-$(openssl rand -hex 3)"
fi
if ! az trustedsigning show --name "$account" --resource-group "$group" > /dev/null 2>&1; then
  echo "Creating the signing account $account in $location (Basic: about 10 USD a month)…"
  az trustedsigning create --name "$account" --resource-group "$group" --location "$location" --sku Basic --only-show-errors > /dev/null
fi
account_id="$(az trustedsigning show --name "$account" --resource-group "$group" --query id -o tsv)"
endpoint="$(az trustedsigning show --name "$account" --resource-group "$group" --query accountUri -o tsv)"
echo "Signing account: $account ($endpoint)"

if [[ -z "$validation" ]]; then
  me="$(az ad signed-in-user show --query id -o tsv)"
  # Requesting an identity validation needs the Identity Verifier role on the account.
  az role assignment create --assignee "$me" --role "Artifact Signing Identity Verifier" --scope "$account_id" --only-show-errors > /dev/null 2>&1 ||
    az role assignment create --assignee "$me" --role "Trusted Signing Identity Verifier" --scope "$account_id" --only-show-errors > /dev/null 2>&1 || true
  cat <<EOF

Next, in the Azure portal (https://portal.azure.com):
  1. Open the signing account "$account" → Identity validations → New → Public.
  2. Fill in your organization (or, in the US/Canada, yourself as an individual developer) and send it.
  3. When it says Completed, copy its id and run:
       $0 $repo <identity-validation-id>
EOF
  exit 0
fi

if ! az trustedsigning certificate-profile show --account-name "$account" --resource-group "$group" --name "$profile" > /dev/null 2>&1; then
  echo "Creating the certificate profile $profile…"
  az trustedsigning certificate-profile create --account-name "$account" --resource-group "$group" --name "$profile" \
    --profile-type PublicTrust --identity-validation-id "$validation" --only-show-errors > /dev/null
fi

# The app the release workflow signs as: may sign with this account, nothing else.
app_id="$(az ad app list --display-name "$app_name" --query '[0].appId' -o tsv)"
if [[ -z "$app_id" ]]; then
  app_id="$(az ad app create --display-name "$app_name" --query appId -o tsv)"
fi
az ad sp show --id "$app_id" > /dev/null 2>&1 || az ad sp create --id "$app_id" > /dev/null
az role assignment create --assignee "$app_id" --role "Artifact Signing Certificate Profile Signer" --scope "$account_id" --only-show-errors > /dev/null 2>&1 ||
  az role assignment create --assignee "$app_id" --role "Trusted Signing Certificate Profile Signer" --scope "$account_id" --only-show-errors > /dev/null
secret="$(az ad app credential reset --id "$app_id" --display-name "github-release" --years 2 --query password -o tsv --only-show-errors)"
tenant="$(az account show --query tenantId -o tsv)"
expires="$(date -d '+2 years' +%Y-%m-%d 2> /dev/null || date -v+2y +%Y-%m-%d)"

if command -v gh > /dev/null && gh auth status > /dev/null 2>&1; then
  gh variable set AZURE_SIGNING_ENDPOINT --repo "$repo" --body "$endpoint"
  gh variable set AZURE_SIGNING_ACCOUNT --repo "$repo" --body "$account"
  gh variable set AZURE_SIGNING_PROFILE --repo "$repo" --body "$profile"
  gh secret set AZURE_TENANT_ID --repo "$repo" --body "$tenant"
  gh secret set AZURE_CLIENT_ID --repo "$repo" --body "$app_id"
  printf '%s' "$secret" | gh secret set AZURE_CLIENT_SECRET --repo "$repo"
  echo "Stored in $repo. The next release is signed. The app's secret expires on $expires: run this again before then."
else
  cat <<EOF

Add these to $repo (Settings → Secrets and variables → Actions):
  variable AZURE_SIGNING_ENDPOINT = $endpoint
  variable AZURE_SIGNING_ACCOUNT  = $account
  variable AZURE_SIGNING_PROFILE  = $profile
  secret   AZURE_TENANT_ID        = $tenant
  secret   AZURE_CLIENT_ID        = $app_id
  secret   AZURE_CLIENT_SECRET    = $secret
The secret expires on $expires: run this again before then.
EOF
fi

#!/bin/sh
# LocalStack ready hook: create the private uploads bucket and its CORS rule.
set -eu

ENDPOINT="http://localhost:4566"
BUCKET="files-local"

aws --endpoint-url "$ENDPOINT" s3api create-bucket --bucket "$BUCKET" --region us-east-1 >/dev/null 2>&1 || true

aws --endpoint-url "$ENDPOINT" s3api put-public-access-block \
  --bucket "$BUCKET" \
  --public-access-block-configuration \
  "BlockPublicAcls=true,IgnorePublicAcls=true,BlockPublicPolicy=true,RestrictPublicBuckets=true"

aws --endpoint-url "$ENDPOINT" s3api put-bucket-cors \
  --bucket "$BUCKET" \
  --cors-configuration file:///etc/localstack/init/ready.d/cors.json

echo "LocalStack S3 ready: private bucket $BUCKET with upload CORS"

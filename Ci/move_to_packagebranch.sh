#!/bin/bash

#set -e
set -x

#echo "git push"
REMOTE=$(git config --get remote.origin.url)
echo $REMOTE
COMMIT=$(git log -1 --pretty=%B)
echo $COMMIT

echo "TARGET_BRANCH is $TARGET_BRANCH"

git archive -o archive.tar HEAD:$FOLDER_TO_EXPORT
ARCHIVE_PATH=$(pwd)

mkdir ../$TARGET_BRANCH
cd ../$TARGET_BRANCH

if [ "$(git ls-remote $REMOTE $TARGET_BRANCH | wc -l)" != 1 ]; then
    git clone --depth=1 $REMOTE
    cd *
    git checkout -b $TARGET_BRANCH
else
    git clone --branch=$TARGET_BRANCH $REMOTE
    cd *
fi

shopt -s extglob
rm ./ -dr -- !(.git)

mv $ARCHIVE_PATH/archive.tar archive.tar

echo "Archive content:"

tar -tf archive.tar
tar -xf archive.tar --overwrite
rm archive.tar

git add -A

echo "Diffs:"
git diff --cached

git config --global user.email "travis@travis-ci.org"
git config --global user.name "Travis CI"

git commit -m "$COMMIT"

git push https://$GITHUB_TOKEN@${REMOTE#*//} $TARGET_BRANCH
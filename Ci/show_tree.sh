#!/bin/bash


shopt -s globstar
for rdir in $1
do
    for file in $rdir/**
    do
      echo "$file"
    done
done

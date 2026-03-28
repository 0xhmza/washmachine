FROM alpine:latest
RUN apk add zsh python3 py3-pip capstone capstone-dev gcc libc-dev python3-dev linux-headers
ADD BDF-ng-stable /bdf
WORKDIR /bdf
RUN zsh install.zsh

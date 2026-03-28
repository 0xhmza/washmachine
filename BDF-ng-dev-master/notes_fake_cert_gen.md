
openssl genrsa -out ca.key 2048
openssl req -new -x509 -days 1826 -key ca.key -out ca.crt
openssl genrsa -out codesign.key 2048
openssl req -new -key codesign.key -reqexts v3_req -out codesign.csr
openssl x509 -req -days 1826 -in codesign.csr -CA ca.crt -CAkey ca.key -set_serial 01 -out codesign.crt
openssl pkcs12 -export -out codesign.pfx -inkey codesign.key -in codesign.crt


#!/bin/bash

# PostgreSQL container'ı ilk başlatıldığında bu script otomatik olarak çalışır
# POSTGRES_MULTIPLE_DATABASES ortam değişkeninden database adlarını okuyup oluşturur

set -e

echo "=== PostgreSQL Init Script Başlatıldı ==="

# POSTGRES_MULTIPLE_DATABASES içindeki veritabanlarını oluştur
if [ -n "$POSTGRES_MULTIPLE_DATABASES" ]; then
    echo "Oluşturulacak veritabanları: $POSTGRES_MULTIPLE_DATABASES"
    
    # Virgülle ayrılmış listeyi boşluk ile ayrılmış hale getir
    for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
        echo "Veritabanı oluşturuluyor: $db"
        
        # psql ile database oluştur
        # -v ON_ERROR_STOP=1 → herhangi bir hata varsa durdur
        # -U "$POSTGRES_USER" → kullanıcı adı kullan
        # postgres → varsayılan database'e bağlan
        psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" postgres <<-EOSQL
            CREATE DATABASE "$db";
EOSQL
        
        echo "✓ Veritabanı başarıyla oluşturuldu: $db"
    done
    
    echo "=== Tüm veritabanları başarıyla oluşturuldu ==="
else
    echo "POSTGRES_MULTIPLE_DATABASES ortam değişkeni boş, hiçbir database oluşturulmadı"
fi
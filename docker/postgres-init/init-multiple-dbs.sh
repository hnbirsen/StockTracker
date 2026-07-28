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
        # Olası boş değerleri atla (örn. sonda fazladan virgül)
        if [ -z "$db" ]; then
            continue
        fi

        echo "Veritabanı oluşturuluyor: $db"

        if psql -U "$POSTGRES_USER" -d postgres -v db_name="$db" -tAc "SELECT 1 FROM pg_database WHERE datname = :'db_name'" | grep -q 1; then
            echo "Database zaten var: $db"
        else
            # psql ile database oluştur
            # -v ON_ERROR_STOP=1 → herhangi bir hata varsa durdur
            # -U "$POSTGRES_USER" → kullanıcı adı kullan
            # postgres → varsayılan database'e bağlan
            psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d postgres <<-EOSQL
                CREATE DATABASE "$db";
EOSQL

            echo "✓ Veritabanı başarıyla oluşturuldu: $db"
        fi
    done
    
    echo "=== Tüm veritabanları başarıyla oluşturuldu ==="
else
    echo "POSTGRES_MULTIPLE_DATABASES ortam değişkeni boş, hiçbir database oluşturulmadı"
fi
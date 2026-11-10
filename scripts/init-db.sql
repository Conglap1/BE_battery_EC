--
-- PostgreSQL database dump
--


-- Dumped from database version 16.15
-- Dumped by pg_dump version 16.15

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: fn_trg_purchase_receipts_prevent_cancel_with_payments(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_trg_purchase_receipts_prevent_cancel_with_payments() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.receipt_status = 'Cancelled' AND OLD.receipt_status <> 'Cancelled' THEN
        IF OLD.paid_amount > 0 OR NEW.paid_amount > 0 OR EXISTS (
            SELECT 1 FROM supplier_payments WHERE purchase_receipt_id = NEW.purchase_receipt_id
        ) THEN
            RAISE EXCEPTION 'Không thể hủy phiếu nhập đã phát sinh thanh toán cho nhà cung cấp (PaidAmount > 0 hoặc đã có chứng từ SupplierPayments). Vui lòng hoàn tiền hoặc xóa chứng từ chi trước.';
        END IF;
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: fn_trg_supplier_payments_sync_receipt_debt(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_trg_supplier_payments_sync_receipt_debt() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_receipt_id BIGINT;
    v_total_paid NUMERIC(18,2);
    v_total_amount NUMERIC(18,2);
    v_status VARCHAR(20);
BEGIN
    IF TG_OP = 'DELETE' THEN
        v_receipt_id := OLD.purchase_receipt_id;
    ELSE
        v_receipt_id := NEW.purchase_receipt_id;
    END IF;

    SELECT receipt_status, total_amount INTO v_status, v_total_amount
    FROM purchase_receipts
    WHERE purchase_receipt_id = v_receipt_id;

    IF v_status = 'Cancelled' AND TG_OP <> 'DELETE' THEN
        RAISE EXCEPTION 'Không được phép ghi nhận thanh toán cho phiếu nhập đã bị hủy.';
    END IF;

    SELECT COALESCE(SUM(amount), 0) INTO v_total_paid
    FROM supplier_payments
    WHERE purchase_receipt_id = v_receipt_id;

    IF v_total_paid > v_total_amount THEN
        RAISE EXCEPTION 'Tổng số tiền thanh toán (%) không được vượt quá tổng giá trị phiếu nhập (%).', v_total_paid, v_total_amount;
    END IF;

    UPDATE purchase_receipts
    SET paid_amount = v_total_paid,
        payment_status = CASE 
            WHEN v_total_paid = 0 THEN 'Unpaid'
            WHEN v_total_paid < total_amount THEN 'PartiallyPaid'
            ELSE 'Paid'
        END,
        updated_at = CURRENT_TIMESTAMP
    WHERE purchase_receipt_id = v_receipt_id;

    RETURN COALESCE(NEW, OLD);
END;
$$;


--
-- Name: fn_trg_users_link_customer_on_register(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_trg_users_link_customer_on_register() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NEW.phone IS NULL THEN
        RETURN NEW;
    END IF;

    -- Trường hợp 1: User cập nhật SĐT nhưng user_id này ĐÃ CÓ trong customers
    IF TG_OP = 'UPDATE' AND EXISTS (SELECT 1 FROM customers WHERE user_id = NEW.user_id) THEN
        UPDATE customers
        SET phone = NEW.phone,
            full_name = COALESCE(customers.full_name, NEW.full_name),
            updated_at = CURRENT_TIMESTAMP
        WHERE user_id = NEW.user_id
          AND phone <> NEW.phone
          AND NOT EXISTS (SELECT 1 FROM customers c2 WHERE c2.phone = NEW.phone AND c2.user_id <> NEW.user_id);
        RETURN NEW;
    END IF;

    -- Trường hợp 2: SĐT này đã từng mua hàng tại quầy (đã có trong customers với user_id IS NULL)
    IF EXISTS (SELECT 1 FROM customers WHERE phone = NEW.phone AND user_id IS NULL) THEN
        UPDATE customers
        SET user_id = NEW.user_id,
            full_name = CASE WHEN full_name IS NULL OR full_name = 'Khách lẻ' THEN NEW.full_name ELSE full_name END,
            updated_at = CURRENT_TIMESTAMP
        WHERE phone = NEW.phone AND user_id IS NULL
          AND NOT EXISTS (SELECT 1 FROM customers c_existing WHERE c_existing.user_id = NEW.user_id);
        RETURN NEW;
    END IF;

    -- Trường hợp 3: Khách hàng mới tinh
    IF NOT EXISTS (SELECT 1 FROM customers WHERE phone = NEW.phone) 
       AND NOT EXISTS (SELECT 1 FROM customers WHERE user_id = NEW.user_id) THEN
        INSERT INTO customers (phone, full_name, user_id, total_spent, created_at)
        VALUES (NEW.phone, NEW.full_name, NEW.user_id, 0, CURRENT_TIMESTAMP);
    END IF;

    RETURN NEW;
END;
$$;


--
-- Name: sp_execute_assembly_order(character varying, bigint, integer, bigint, character varying, bigint); Type: PROCEDURE; Schema: public; Owner: -
--

CREATE PROCEDURE public.sp_execute_assembly_order(IN p_assembly_code character varying, IN p_bundle_variant_id bigint, IN p_quantity_produced integer, IN p_user_id bigint, IN p_note character varying, INOUT p_assembly_order_id bigint DEFAULT NULL::bigint)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_missing_components TEXT := '';
    v_total_cost NUMERIC(18,2) := 0;
    v_unit_cost NUMERIC(18,2) := 0;
    v_rec RECORD;
BEGIN
    IF p_quantity_produced <= 0 THEN
        RAISE EXCEPTION 'Số lượng sản phẩm lắp ráp phải lớn hơn 0.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM product_variant_bundles WHERE bundle_variant_id = p_bundle_variant_id) THEN
        RAISE EXCEPTION 'Khối pin này chưa được định nghĩa định mức linh kiện (BOM) trong product_variant_bundles.';
    END IF;

    IF EXISTS (SELECT 1 FROM assembly_orders WHERE assembly_code = p_assembly_code) THEN
        RAISE EXCEPTION 'Mã lệnh lắp ráp % đã tồn tại. Không được thực hiện lặp.', p_assembly_code;
    END IF;

    -- Khóa dòng các linh kiện tiêu hao trong inventory để chống race condition
    PERFORM 1 FROM inventory 
    WHERE product_variant_id IN (
        SELECT component_variant_id 
        FROM product_variant_bundles 
        WHERE bundle_variant_id = p_bundle_variant_id
    )
    FOR UPDATE;

    -- Kiểm tra tồn kho khả dụng linh kiện
    SELECT string_agg(
        pv.sku || ' (Tồn: ' || (COALESCE(inv.quantity_on_hand, 0) - COALESCE(inv.quantity_reserved, 0))::TEXT || ', Cần: ' || (b.quantity * p_quantity_produced)::TEXT || ')',
        ', '
    )
    INTO v_missing_components
    FROM product_variant_bundles b
    JOIN product_variants pv ON b.component_variant_id = pv.product_variant_id
    LEFT JOIN inventory inv ON b.component_variant_id = inv.product_variant_id
    WHERE b.bundle_variant_id = p_bundle_variant_id
      AND (inv.product_variant_id IS NULL OR (inv.quantity_on_hand - inv.quantity_reserved) < (b.quantity * p_quantity_produced));

    IF v_missing_components IS NOT NULL AND LENGTH(v_missing_components) > 0 THEN
        RAISE EXCEPTION 'Kho không đủ linh kiện để lắp ráp: %', v_missing_components;
    END IF;

    -- Tính tổng giá vốn linh kiện tiêu hao
    SELECT COALESCE(SUM(b.quantity * p_quantity_produced * COALESCE(pv.cost_price, 0)), 0)
    INTO v_total_cost
    FROM product_variant_bundles b
    JOIN product_variants pv ON b.component_variant_id = pv.product_variant_id
    WHERE b.bundle_variant_id = p_bundle_variant_id;

    -- Tạo lệnh lắp ráp
    INSERT INTO assembly_orders (assembly_code, bundle_variant_id, quantity_produced, total_cost, status, note, created_by_user_id, assembled_at)
    VALUES (p_assembly_code, p_bundle_variant_id, p_quantity_produced, v_total_cost, 'Completed', p_note, p_user_id, CURRENT_TIMESTAMP)
    RETURNING assembly_order_id INTO p_assembly_order_id;

    -- Chụp snapshot BOM vào assembly_order_items
    INSERT INTO assembly_order_items (assembly_order_id, component_variant_id, quantity_consumed, unit_cost)
    SELECT 
        p_assembly_order_id,
        b.component_variant_id,
        b.quantity * p_quantity_produced,
        COALESCE(pv.cost_price, 0)
    FROM product_variant_bundles b
    JOIN product_variants pv ON b.component_variant_id = pv.product_variant_id
    WHERE b.bundle_variant_id = p_bundle_variant_id;

    -- Trừ tồn kho linh kiện và ghi sổ thẻ kho AssemblyConsumption
    FOR v_rec IN 
        SELECT b.component_variant_id, (b.quantity * p_quantity_produced) AS qty, COALESCE(pv.cost_price, 0) AS cost
        FROM product_variant_bundles b
        JOIN product_variants pv ON b.component_variant_id = pv.product_variant_id
        WHERE b.bundle_variant_id = p_bundle_variant_id
    LOOP
        UPDATE inventory
        SET quantity_on_hand = quantity_on_hand - v_rec.qty,
            version = version + 1,
            updated_at = CURRENT_TIMESTAMP
        WHERE product_variant_id = v_rec.component_variant_id;

        INSERT INTO inventory_transactions (product_variant_id, transaction_type, quantity_change, reference_type, reference_id, unit_cost, note, created_by_user_id, created_at)
        VALUES (v_rec.component_variant_id, 'AssemblyConsumption', -v_rec.qty, 'assembly_orders', p_assembly_order_id, v_rec.cost, 'Xuất linh kiện đóng khối pin lệnh ' || p_assembly_code, p_user_id, CURRENT_TIMESTAMP);
    END LOOP;

    -- Tăng tồn kho thành phẩm
    INSERT INTO inventory (product_variant_id, quantity_on_hand, quantity_reserved, reorder_level, updated_at, version)
    VALUES (p_bundle_variant_id, p_quantity_produced, 0, 0, CURRENT_TIMESTAMP, 1)
    ON CONFLICT (product_variant_id) DO UPDATE 
    SET quantity_on_hand = inventory.quantity_on_hand + EXCLUDED.quantity_on_hand,
        version = inventory.version + 1,
        updated_at = CURRENT_TIMESTAMP;

    v_unit_cost := ROUND(v_total_cost / p_quantity_produced, 2);

    INSERT INTO inventory_transactions (product_variant_id, transaction_type, quantity_change, reference_type, reference_id, unit_cost, note, created_by_user_id, created_at)
    VALUES (p_bundle_variant_id, 'AssemblyProduction', p_quantity_produced, 'assembly_orders', p_assembly_order_id, v_unit_cost, 'Nhập kho thành phẩm lệnh ' || p_assembly_code, p_user_id, CURRENT_TIMESTAMP);

    -- Cập nhật giá vốn chuẩn cho biến thể khối pin
    UPDATE product_variants
    SET cost_price = v_unit_cost,
        updated_at = CURRENT_TIMESTAMP
    WHERE product_variant_id = p_bundle_variant_id;
END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: addresses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.addresses (
    address_id bigint NOT NULL,
    user_id bigint NOT NULL,
    recipient_name character varying(100) NOT NULL,
    phone character varying(20) NOT NULL,
    province character varying(100) NOT NULL,
    district character varying(100) NOT NULL,
    ward character varying(100) NOT NULL,
    street_address character varying(255) NOT NULL,
    is_default boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: addresses_address_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.addresses ALTER COLUMN address_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.addresses_address_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: assembly_order_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.assembly_order_items (
    assembly_order_item_id bigint NOT NULL,
    assembly_order_id bigint NOT NULL,
    component_variant_id bigint NOT NULL,
    quantity_consumed integer NOT NULL,
    unit_cost numeric(18,2) NOT NULL,
    line_total numeric(18,2) GENERATED ALWAYS AS (((quantity_consumed)::numeric * unit_cost)) STORED,
    CONSTRAINT assembly_order_items_quantity_consumed_check CHECK ((quantity_consumed > 0)),
    CONSTRAINT assembly_order_items_unit_cost_check CHECK ((unit_cost >= (0)::numeric))
);


--
-- Name: assembly_order_items_assembly_order_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.assembly_order_items ALTER COLUMN assembly_order_item_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.assembly_order_items_assembly_order_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: assembly_orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.assembly_orders (
    assembly_order_id bigint NOT NULL,
    assembly_code character varying(40) NOT NULL,
    bundle_variant_id bigint NOT NULL,
    quantity_produced integer NOT NULL,
    total_cost numeric(18,2) DEFAULT 0 NOT NULL,
    unit_cost numeric(18,2) GENERATED ALWAYS AS (round((total_cost / (NULLIF(quantity_produced, 0))::numeric), 2)) STORED,
    status character varying(20) DEFAULT 'Completed'::character varying NOT NULL,
    note character varying(500),
    created_by_user_id bigint NOT NULL,
    assembled_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT assembly_orders_quantity_produced_check CHECK ((quantity_produced > 0)),
    CONSTRAINT assembly_orders_status_check CHECK (((status)::text = ANY ((ARRAY['Completed'::character varying, 'Cancelled'::character varying])::text[]))),
    CONSTRAINT assembly_orders_total_cost_check CHECK ((total_cost >= (0)::numeric))
);


--
-- Name: assembly_orders_assembly_order_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.assembly_orders ALTER COLUMN assembly_order_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.assembly_orders_assembly_order_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: attribute_values; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.attribute_values (
    attribute_value_id integer NOT NULL,
    attribute_id integer NOT NULL,
    value_name character varying(100) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: attribute_values_attribute_value_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.attribute_values ALTER COLUMN attribute_value_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.attribute_values_attribute_value_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: attributes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.attributes (
    attribute_id integer NOT NULL,
    attribute_name character varying(100) NOT NULL,
    attribute_code character varying(50) NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: attributes_attribute_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.attributes ALTER COLUMN attribute_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.attributes_attribute_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: categories; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.categories (
    category_id integer NOT NULL,
    parent_category_id integer,
    category_name character varying(100) NOT NULL,
    slug character varying(120) NOT NULL,
    description character varying(500),
    is_active boolean DEFAULT true NOT NULL,
    display_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: categories_category_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.categories ALTER COLUMN category_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.categories_category_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: customers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.customers (
    customer_id bigint NOT NULL,
    phone character varying(20) NOT NULL,
    full_name character varying(100),
    user_id bigint,
    total_spent numeric(18,2) DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT customers_total_spent_check CHECK ((total_spent >= (0)::numeric))
);


--
-- Name: customers_customer_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.customers ALTER COLUMN customer_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.customers_customer_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: inventory; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory (
    product_variant_id bigint NOT NULL,
    quantity_on_hand integer DEFAULT 0 NOT NULL,
    quantity_reserved integer DEFAULT 0 NOT NULL,
    reorder_level integer DEFAULT 0 NOT NULL,
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    version integer DEFAULT 1 NOT NULL,
    CONSTRAINT ck_inventory_reserved_limit CHECK ((quantity_reserved <= quantity_on_hand)),
    CONSTRAINT inventory_quantity_on_hand_check CHECK ((quantity_on_hand >= 0)),
    CONSTRAINT inventory_quantity_reserved_check CHECK ((quantity_reserved >= 0)),
    CONSTRAINT inventory_reorder_level_check CHECK ((reorder_level >= 0))
);


--
-- Name: inventory_transactions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventory_transactions (
    inventory_transaction_id bigint NOT NULL,
    product_variant_id bigint NOT NULL,
    transaction_type character varying(30) NOT NULL,
    quantity_change integer NOT NULL,
    reference_type character varying(50) NOT NULL,
    reference_id bigint NOT NULL,
    unit_cost numeric(18,2) DEFAULT 0 NOT NULL,
    note character varying(500),
    created_by_user_id bigint NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT inventory_transactions_quantity_change_check CHECK ((quantity_change <> 0)),
    CONSTRAINT inventory_transactions_transaction_type_check CHECK (((transaction_type)::text = ANY ((ARRAY['PurchaseReceipt'::character varying, 'PurchaseReturn'::character varying, 'OnlineSale'::character varying, 'CounterSale'::character varying, 'SaleReturn'::character varying, 'RepairConsumption'::character varying, 'RepairReturn'::character varying, 'AssemblyConsumption'::character varying, 'AssemblyProduction'::character varying, 'StockAudit'::character varying, 'InitialBalance'::character varying])::text[]))),
    CONSTRAINT inventory_transactions_unit_cost_check CHECK ((unit_cost >= (0)::numeric))
);


--
-- Name: inventory_transactions_inventory_transaction_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.inventory_transactions ALTER COLUMN inventory_transaction_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.inventory_transactions_inventory_transaction_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: order_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.order_items (
    order_item_id bigint NOT NULL,
    order_id bigint NOT NULL,
    product_variant_id bigint NOT NULL,
    product_name character varying(200) NOT NULL,
    variant_name character varying(150) NOT NULL,
    sku character varying(50) NOT NULL,
    unit_price numeric(18,2) NOT NULL,
    cost_price numeric(18,2) DEFAULT 0 NOT NULL,
    quantity integer NOT NULL,
    warranty_months integer DEFAULT 0 NOT NULL,
    line_total numeric(18,2) GENERATED ALWAYS AS (((quantity)::numeric * unit_price)) STORED,
    CONSTRAINT order_items_cost_price_check CHECK ((cost_price >= (0)::numeric)),
    CONSTRAINT order_items_quantity_check CHECK ((quantity > 0)),
    CONSTRAINT order_items_unit_price_check CHECK ((unit_price >= (0)::numeric)),
    CONSTRAINT order_items_warranty_months_check CHECK ((warranty_months >= 0))
);


--
-- Name: order_items_order_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.order_items ALTER COLUMN order_item_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.order_items_order_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: orders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.orders (
    order_id bigint NOT NULL,
    order_code character varying(40) NOT NULL,
    user_id bigint NOT NULL,
    recipient_name character varying(100) NOT NULL,
    recipient_phone character varying(20) NOT NULL,
    shipping_address character varying(255) NOT NULL,
    shipping_province character varying(100) DEFAULT ''::character varying,
    shipping_district character varying(100) DEFAULT ''::character varying,
    shipping_ward character varying(100) DEFAULT ''::character varying,
    shipping_method_id integer NOT NULL,
    voucher_id integer,
    subtotal_amount numeric(18,2) NOT NULL,
    discount_amount numeric(18,2) DEFAULT 0 NOT NULL,
    shipping_fee numeric(18,2) DEFAULT 0 NOT NULL,
    total_amount numeric(18,2) NOT NULL,
    order_status character varying(30) DEFAULT 'PendingConfirmation'::character varying NOT NULL,
    payment_status character varying(30) DEFAULT 'Pending'::character varying NOT NULL,
    customer_note character varying(500),
    staff_note character varying(500),
    confirmed_by_user_id bigint,
    placed_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    confirmed_at timestamp with time zone,
    completed_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    admin_note character varying(1000),
    payment_method character varying(30) DEFAULT 'COD'::character varying,
    subtotal numeric(18,2) DEFAULT 0,
    CONSTRAINT orders_discount_amount_check CHECK ((discount_amount >= (0)::numeric)),
    CONSTRAINT orders_order_status_check CHECK (((order_status)::text = ANY ((ARRAY['PendingConfirmation'::character varying, 'Confirmed'::character varying, 'Processing'::character varying, 'Shipped'::character varying, 'Completed'::character varying, 'Cancelled'::character varying, 'Returned'::character varying])::text[]))),
    CONSTRAINT orders_payment_status_check CHECK (((payment_status)::text = ANY ((ARRAY['Pending'::character varying, 'Paid'::character varying, 'Failed'::character varying, 'Refunded'::character varying])::text[]))),
    CONSTRAINT orders_shipping_fee_check CHECK ((shipping_fee >= (0)::numeric)),
    CONSTRAINT orders_subtotal_amount_check CHECK ((subtotal_amount >= (0)::numeric)),
    CONSTRAINT orders_total_amount_check CHECK ((total_amount >= (0)::numeric))
);


--
-- Name: orders_order_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.orders ALTER COLUMN order_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.orders_order_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.payments (
    payment_id bigint NOT NULL,
    order_id bigint NOT NULL,
    payment_method character varying(30) NOT NULL,
    transaction_code character varying(100),
    amount numeric(18,2) NOT NULL,
    payment_status character varying(20) DEFAULT 'Pending'::character varying NOT NULL,
    paid_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    provider character varying(100),
    CONSTRAINT payments_amount_check CHECK ((amount >= (0)::numeric)),
    CONSTRAINT payments_payment_method_check CHECK (((payment_method)::text = ANY ((ARRAY['COD'::character varying, 'VNPay'::character varying, 'MoMo'::character varying, 'BankTransfer'::character varying])::text[]))),
    CONSTRAINT payments_payment_status_check CHECK (((payment_status)::text = ANY ((ARRAY['Pending'::character varying, 'Success'::character varying, 'Failed'::character varying])::text[])))
);


--
-- Name: payments_payment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.payments ALTER COLUMN payment_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.payments_payment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: product_images; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_images (
    image_id bigint NOT NULL,
    product_id bigint NOT NULL,
    image_url character varying(500) NOT NULL,
    is_thumbnail boolean DEFAULT false NOT NULL,
    display_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: product_images_image_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.product_images ALTER COLUMN image_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.product_images_image_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: product_variant_attributes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_variant_attributes (
    product_variant_id bigint NOT NULL,
    attribute_value_id integer NOT NULL
);


--
-- Name: product_variant_bundles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_variant_bundles (
    bundle_variant_id bigint NOT NULL,
    component_variant_id bigint NOT NULL,
    quantity integer NOT NULL,
    CONSTRAINT ck_product_variant_bundles_no_self_loop CHECK ((bundle_variant_id <> component_variant_id)),
    CONSTRAINT product_variant_bundles_quantity_check CHECK ((quantity > 0))
);


--
-- Name: product_variants; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.product_variants (
    product_variant_id bigint NOT NULL,
    product_id bigint NOT NULL,
    sku character varying(50) NOT NULL,
    variant_name character varying(150) NOT NULL,
    price numeric(18,2) NOT NULL,
    cost_price numeric(18,2) DEFAULT 0 NOT NULL,
    weight_grams integer DEFAULT 0 NOT NULL,
    warranty_months integer DEFAULT 0 NOT NULL,
    is_bundle boolean DEFAULT false NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    option_summary character varying(255),
    unit character varying(50) DEFAULT 'Cái'::character varying NOT NULL,
    image_url character varying(500),
    CONSTRAINT product_variants_cost_price_check CHECK ((cost_price >= (0)::numeric)),
    CONSTRAINT product_variants_price_check CHECK ((price >= (0)::numeric)),
    CONSTRAINT product_variants_warranty_months_check CHECK ((warranty_months >= 0)),
    CONSTRAINT product_variants_weight_grams_check CHECK ((weight_grams >= 0))
);


--
-- Name: product_variants_product_variant_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.product_variants ALTER COLUMN product_variant_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.product_variants_product_variant_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: products; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.products (
    product_id bigint NOT NULL,
    category_id integer NOT NULL,
    product_name character varying(200) NOT NULL,
    slug character varying(220) NOT NULL,
    description text,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    brand character varying(100),
    short_description character varying(500),
    is_featured boolean DEFAULT false NOT NULL
);


--
-- Name: products_product_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.products ALTER COLUMN product_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.products_product_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: purchase_receipt_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_receipt_items (
    purchase_receipt_item_id bigint NOT NULL,
    purchase_receipt_id bigint NOT NULL,
    product_variant_id bigint NOT NULL,
    quantity integer NOT NULL,
    unit_cost numeric(18,2) NOT NULL,
    line_total numeric(18,2) GENERATED ALWAYS AS (((quantity)::numeric * unit_cost)) STORED,
    CONSTRAINT purchase_receipt_items_quantity_check CHECK ((quantity > 0)),
    CONSTRAINT purchase_receipt_items_unit_cost_check CHECK ((unit_cost >= (0)::numeric))
);


--
-- Name: purchase_receipt_items_purchase_receipt_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.purchase_receipt_items ALTER COLUMN purchase_receipt_item_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.purchase_receipt_items_purchase_receipt_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: purchase_receipts; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.purchase_receipts (
    purchase_receipt_id bigint NOT NULL,
    receipt_code character varying(50) NOT NULL,
    supplier_id integer NOT NULL,
    received_by_user_id bigint NOT NULL,
    receipt_status character varying(20) DEFAULT 'Completed'::character varying NOT NULL,
    payment_status character varying(20) DEFAULT 'Unpaid'::character varying NOT NULL,
    total_amount numeric(18,2) DEFAULT 0 NOT NULL,
    paid_amount numeric(18,2) DEFAULT 0 NOT NULL,
    note character varying(500),
    receipt_date timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT purchase_receipts_paid_amount_check CHECK ((paid_amount >= (0)::numeric)),
    CONSTRAINT purchase_receipts_payment_status_check CHECK (((payment_status)::text = ANY ((ARRAY['Unpaid'::character varying, 'PartiallyPaid'::character varying, 'Paid'::character varying])::text[]))),
    CONSTRAINT purchase_receipts_receipt_status_check CHECK (((receipt_status)::text = ANY ((ARRAY['Completed'::character varying, 'Cancelled'::character varying])::text[]))),
    CONSTRAINT purchase_receipts_total_amount_check CHECK ((total_amount >= (0)::numeric))
);


--
-- Name: purchase_receipts_purchase_receipt_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.purchase_receipts ALTER COLUMN purchase_receipt_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.purchase_receipts_purchase_receipt_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: repair_ticket_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.repair_ticket_items (
    repair_ticket_item_id bigint NOT NULL,
    repair_ticket_id bigint NOT NULL,
    product_variant_id bigint NOT NULL,
    quantity integer NOT NULL,
    unit_price numeric(18,2) DEFAULT 0 NOT NULL,
    line_total numeric(18,2) GENERATED ALWAYS AS (((quantity)::numeric * unit_price)) STORED,
    CONSTRAINT repair_ticket_items_quantity_check CHECK ((quantity > 0)),
    CONSTRAINT repair_ticket_items_unit_price_check CHECK ((unit_price >= (0)::numeric))
);


--
-- Name: repair_ticket_items_repair_ticket_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.repair_ticket_items ALTER COLUMN repair_ticket_item_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.repair_ticket_items_repair_ticket_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: repair_tickets; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.repair_tickets (
    repair_ticket_id bigint NOT NULL,
    ticket_code character varying(40) NOT NULL,
    customer_id bigint,
    customer_name character varying(100) NOT NULL,
    customer_phone character varying(20) NOT NULL,
    device_model character varying(150) NOT NULL,
    serial_or_pin_code character varying(100),
    issue_description character varying(500) NOT NULL,
    solution_note character varying(500),
    estimated_cost numeric(18,2) DEFAULT 0 NOT NULL,
    final_cost numeric(18,2) DEFAULT 0 NOT NULL,
    warranty_months integer DEFAULT 0 NOT NULL,
    ticket_status character varying(30) DEFAULT 'Received'::character varying NOT NULL,
    payment_status character varying(20) DEFAULT 'Unpaid'::character varying NOT NULL,
    payment_method character varying(30),
    created_by_user_id bigint NOT NULL,
    technician_user_id bigint,
    received_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    completed_at timestamp with time zone,
    delivered_at timestamp with time zone,
    cancelled_at timestamp with time zone,
    paid_amount numeric(18,2) DEFAULT 0,
    CONSTRAINT repair_tickets_estimated_cost_check CHECK ((estimated_cost >= (0)::numeric)),
    CONSTRAINT repair_tickets_final_cost_check CHECK ((final_cost >= (0)::numeric)),
    CONSTRAINT repair_tickets_payment_method_check CHECK (((payment_method)::text = ANY ((ARRAY['Cash'::character varying, 'Card'::character varying, 'BankTransfer'::character varying])::text[]))),
    CONSTRAINT repair_tickets_payment_status_check CHECK (((payment_status)::text = ANY ((ARRAY['Unpaid'::character varying, 'Paid'::character varying])::text[]))),
    CONSTRAINT repair_tickets_ticket_status_check CHECK (((ticket_status)::text = ANY ((ARRAY['Received'::character varying, 'Diagnosing'::character varying, 'Quoted'::character varying, 'Repairing'::character varying, 'Completed'::character varying, 'Delivered'::character varying, 'Cancelled'::character varying])::text[]))),
    CONSTRAINT repair_tickets_warranty_months_check CHECK ((warranty_months >= 0))
);


--
-- Name: repair_tickets_repair_ticket_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.repair_tickets ALTER COLUMN repair_ticket_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.repair_tickets_repair_ticket_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: reviews; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reviews (
    review_id bigint NOT NULL,
    user_id bigint NOT NULL,
    product_id bigint NOT NULL,
    order_item_id bigint NOT NULL,
    rating integer NOT NULL,
    comment character varying(1000),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT reviews_rating_check CHECK (((rating >= 1) AND (rating <= 5)))
);


--
-- Name: reviews_review_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.reviews ALTER COLUMN review_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.reviews_review_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.roles (
    role_id integer NOT NULL,
    role_name character varying(50) NOT NULL,
    description character varying(255),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: roles_role_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.roles ALTER COLUMN role_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.roles_role_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: sale_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sale_items (
    sale_item_id bigint NOT NULL,
    sale_id bigint NOT NULL,
    product_variant_id bigint NOT NULL,
    product_name character varying(200) NOT NULL,
    variant_name character varying(150) NOT NULL,
    sku character varying(50) NOT NULL,
    unit_price numeric(18,2) NOT NULL,
    cost_price numeric(18,2) DEFAULT 0 NOT NULL,
    quantity integer NOT NULL,
    warranty_months integer DEFAULT 0 NOT NULL,
    line_total numeric(18,2) GENERATED ALWAYS AS (((quantity)::numeric * unit_price)) STORED,
    CONSTRAINT sale_items_cost_price_check CHECK ((cost_price >= (0)::numeric)),
    CONSTRAINT sale_items_quantity_check CHECK ((quantity > 0)),
    CONSTRAINT sale_items_unit_price_check CHECK ((unit_price >= (0)::numeric)),
    CONSTRAINT sale_items_warranty_months_check CHECK ((warranty_months >= 0))
);


--
-- Name: sale_items_sale_item_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.sale_items ALTER COLUMN sale_item_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.sale_items_sale_item_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: sales; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.sales (
    sale_id bigint NOT NULL,
    sale_code character varying(40) NOT NULL,
    sold_by_user_id bigint NOT NULL,
    customer_id bigint,
    customer_name character varying(100),
    customer_phone character varying(20),
    subtotal_amount numeric(18,2) NOT NULL,
    discount_amount numeric(18,2) DEFAULT 0 NOT NULL,
    total_amount numeric(18,2) NOT NULL,
    payment_method character varying(30) DEFAULT 'Cash'::character varying NOT NULL,
    sale_status character varying(20) DEFAULT 'Completed'::character varying NOT NULL,
    note character varying(500),
    sold_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT sales_discount_amount_check CHECK ((discount_amount >= (0)::numeric)),
    CONSTRAINT sales_payment_method_check CHECK (((payment_method)::text = ANY ((ARRAY['Cash'::character varying, 'Card'::character varying, 'BankTransfer'::character varying])::text[]))),
    CONSTRAINT sales_sale_status_check CHECK (((sale_status)::text = ANY ((ARRAY['Completed'::character varying, 'Cancelled'::character varying])::text[]))),
    CONSTRAINT sales_subtotal_amount_check CHECK ((subtotal_amount >= (0)::numeric)),
    CONSTRAINT sales_total_amount_check CHECK ((total_amount >= (0)::numeric))
);


--
-- Name: sales_sale_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.sales ALTER COLUMN sale_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.sales_sale_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: shipments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shipments (
    shipment_id bigint NOT NULL,
    order_id bigint NOT NULL,
    shipping_method_id integer NOT NULL,
    tracking_code character varying(100),
    shipment_status character varying(30) DEFAULT 'Pending'::character varying NOT NULL,
    shipped_at timestamp with time zone,
    delivered_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    carrier_name character varying(100),
    tracking_number character varying(100),
    shipping_fee numeric(18,2) DEFAULT 0,
    note character varying(500),
    CONSTRAINT shipments_shipment_status_check CHECK (((shipment_status)::text = ANY ((ARRAY['Pending'::character varying, 'PickedUp'::character varying, 'InTransit'::character varying, 'Delivered'::character varying, 'FailedDelivery'::character varying, 'Returned'::character varying])::text[])))
);


--
-- Name: shipments_shipment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.shipments ALTER COLUMN shipment_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.shipments_shipment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: shipping_methods; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shipping_methods (
    shipping_method_id integer NOT NULL,
    method_name character varying(100) NOT NULL,
    carrier_name character varying(100) NOT NULL,
    base_fee numeric(18,2) DEFAULT 0 NOT NULL,
    estimated_delivery_days character varying(50),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT shipping_methods_base_fee_check CHECK ((base_fee >= (0)::numeric))
);


--
-- Name: shipping_methods_shipping_method_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.shipping_methods ALTER COLUMN shipping_method_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.shipping_methods_shipping_method_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: shop_expenses; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.shop_expenses (
    expense_id bigint NOT NULL,
    expense_code character varying(40) NOT NULL,
    expense_category character varying(100) NOT NULL,
    amount numeric(18,2) NOT NULL,
    paid_by_user_id bigint NOT NULL,
    note character varying(500),
    paid_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT shop_expenses_amount_check CHECK ((amount > (0)::numeric))
);


--
-- Name: shop_expenses_expense_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.shop_expenses ALTER COLUMN expense_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.shop_expenses_expense_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: store_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.store_settings (
    setting_id integer NOT NULL,
    setting_key character varying(100) NOT NULL,
    setting_value text NOT NULL,
    description character varying(255),
    updated_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: store_settings_setting_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.store_settings ALTER COLUMN setting_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.store_settings_setting_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: supplier_payments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.supplier_payments (
    supplier_payment_id bigint NOT NULL,
    payment_code character varying(50) NOT NULL,
    purchase_receipt_id bigint NOT NULL,
    created_by_user_id bigint NOT NULL,
    amount numeric(18,2) NOT NULL,
    payment_method character varying(30) NOT NULL,
    payment_date timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    note character varying(500),
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    CONSTRAINT supplier_payments_amount_check CHECK ((amount > (0)::numeric)),
    CONSTRAINT supplier_payments_payment_method_check CHECK (((payment_method)::text = ANY ((ARRAY['Cash'::character varying, 'BankTransfer'::character varying, 'Other'::character varying])::text[])))
);


--
-- Name: supplier_payments_supplier_payment_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.supplier_payments ALTER COLUMN supplier_payment_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.supplier_payments_supplier_payment_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: suppliers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.suppliers (
    supplier_id integer NOT NULL,
    supplier_code character varying(50) NOT NULL,
    name character varying(150) NOT NULL,
    contact_name character varying(100),
    phone character varying(20) NOT NULL,
    email character varying(100),
    address character varying(255),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone
);


--
-- Name: suppliers_supplier_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.suppliers ALTER COLUMN supplier_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.suppliers_supplier_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: user_roles; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.user_roles (
    user_id bigint NOT NULL,
    role_id integer NOT NULL,
    assigned_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL
);


--
-- Name: users; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.users (
    user_id bigint NOT NULL,
    username character varying(50) NOT NULL,
    password_hash character varying(255) NOT NULL,
    email character varying(100) NOT NULL,
    phone character varying(20),
    full_name character varying(100) NOT NULL,
    avatar_url character varying(255),
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    refresh_token character varying(500),
    refresh_token_expiry_time timestamp with time zone
);


--
-- Name: users_user_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.users ALTER COLUMN user_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.users_user_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: vouchers; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.vouchers (
    voucher_id integer NOT NULL,
    voucher_code character varying(50) NOT NULL,
    description character varying(255),
    discount_type character varying(20) NOT NULL,
    discount_value numeric(18,2) NOT NULL,
    min_order_amount numeric(18,2) DEFAULT 0 NOT NULL,
    max_discount_amount numeric(18,2),
    usage_limit integer,
    used_count integer DEFAULT 0 NOT NULL,
    start_date timestamp with time zone NOT NULL,
    end_date timestamp with time zone NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at timestamp with time zone,
    CONSTRAINT ck_vouchers_date_range CHECK ((end_date >= start_date)),
    CONSTRAINT vouchers_discount_type_check CHECK (((discount_type)::text = ANY ((ARRAY['Percentage'::character varying, 'FixedAmount'::character varying])::text[]))),
    CONSTRAINT vouchers_discount_value_check CHECK ((discount_value > (0)::numeric)),
    CONSTRAINT vouchers_max_discount_amount_check CHECK ((max_discount_amount > (0)::numeric)),
    CONSTRAINT vouchers_min_order_amount_check CHECK ((min_order_amount >= (0)::numeric)),
    CONSTRAINT vouchers_usage_limit_check CHECK ((usage_limit > 0)),
    CONSTRAINT vouchers_used_count_check CHECK ((used_count >= 0))
);


--
-- Name: vouchers_voucher_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

ALTER TABLE public.vouchers ALTER COLUMN voucher_id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.vouchers_voucher_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- Name: vw_inventory_reconciliation; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_inventory_reconciliation AS
 WITH transactionsummary AS (
         SELECT it.product_variant_id,
            COALESCE(sum(it.quantity_change), (0)::bigint) AS total_transaction_quantity
           FROM public.inventory_transactions it
          GROUP BY it.product_variant_id
        )
 SELECT pv.product_variant_id,
    pv.sku,
    pv.variant_name,
    COALESCE(inv.quantity_on_hand, 0) AS inventory_quantity,
    COALESCE(ts.total_transaction_quantity, (0)::bigint) AS transaction_quantity,
    (COALESCE(inv.quantity_on_hand, 0) - COALESCE(ts.total_transaction_quantity, (0)::bigint)) AS discrepancy,
        CASE
            WHEN ((COALESCE(inv.quantity_on_hand, 0) - COALESCE(ts.total_transaction_quantity, (0)::bigint)) = 0) THEN 'Khớp chuẩn 100%'::text
            ELSE 'Lệch tồn kho cần kiểm toán'::text
        END AS reconciliation_status
   FROM ((public.product_variants pv
     LEFT JOIN public.inventory inv ON ((pv.product_variant_id = inv.product_variant_id)))
     LEFT JOIN transactionsummary ts ON ((pv.product_variant_id = ts.product_variant_id)));


--
-- Name: vw_supplier_debt_summary; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_supplier_debt_summary AS
 SELECT s.supplier_id,
    s.supplier_code,
    s.name AS supplier_name,
    s.phone,
    count(pr.purchase_receipt_id) AS total_receipts,
    COALESCE(sum(pr.total_amount), (0)::numeric) AS total_receipt_amount,
    COALESCE(sum(pr.paid_amount), (0)::numeric) AS total_paid_amount,
    (COALESCE(sum(pr.total_amount), (0)::numeric) - COALESCE(sum(pr.paid_amount), (0)::numeric)) AS total_remaining_debt,
    count(
        CASE
            WHEN ((pr.payment_status)::text = 'Unpaid'::text) THEN 1
            ELSE NULL::integer
        END) AS unpaid_receipt_count,
    count(
        CASE
            WHEN ((pr.payment_status)::text = 'PartiallyPaid'::text) THEN 1
            ELSE NULL::integer
        END) AS partially_paid_receipt_count,
    count(
        CASE
            WHEN ((pr.payment_status)::text = 'Paid'::text) THEN 1
            ELSE NULL::integer
        END) AS paid_receipt_count
   FROM (public.suppliers s
     LEFT JOIN public.purchase_receipts pr ON (((s.supplier_id = pr.supplier_id) AND ((pr.receipt_status)::text <> 'Cancelled'::text))))
  GROUP BY s.supplier_id, s.supplier_code, s.name, s.phone;


--
-- Name: vw_total_revenue_summary; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_total_revenue_summary AS
 WITH onlineordersagg AS (
         SELECT o.order_id,
            (COALESCE(o.completed_at, s.delivered_at, o.placed_at))::date AS sale_date,
            o.order_status,
            o.total_amount,
            COALESCE(cogs.total_cost, (0)::numeric) AS order_cogs
           FROM ((public.orders o
             LEFT JOIN public.shipments s ON (((o.order_id = s.order_id) AND ((s.shipment_status)::text = 'Delivered'::text))))
             LEFT JOIN ( SELECT oi.order_id,
                    sum(((oi.quantity)::numeric * COALESCE(oi.cost_price, pv.cost_price))) AS total_cost
                   FROM (public.order_items oi
                     LEFT JOIN public.product_variants pv ON ((oi.product_variant_id = pv.product_variant_id)))
                  GROUP BY oi.order_id) cogs ON ((o.order_id = cogs.order_id)))
        ), onlinedaily AS (
         SELECT onlineordersagg.sale_date,
            count(*) AS online_order_count,
            sum(
                CASE
                    WHEN ((onlineordersagg.order_status)::text = 'Completed'::text) THEN onlineordersagg.total_amount
                    ELSE (0)::numeric
                END) AS online_completed_revenue,
            sum(
                CASE
                    WHEN ((onlineordersagg.order_status)::text = 'Completed'::text) THEN onlineordersagg.order_cogs
                    ELSE (0)::numeric
                END) AS online_completed_cogs,
            sum(
                CASE
                    WHEN ((onlineordersagg.order_status)::text = 'Cancelled'::text) THEN 1
                    ELSE 0
                END) AS online_cancelled_count
           FROM onlineordersagg
          GROUP BY onlineordersagg.sale_date
        ), countersalesagg AS (
         SELECT s.sale_id,
            (s.sold_at)::date AS sale_date,
            s.sale_status,
            s.total_amount,
            COALESCE(cogs.total_cost, (0)::numeric) AS sale_cogs
           FROM (public.sales s
             LEFT JOIN ( SELECT si.sale_id,
                    sum(((si.quantity)::numeric * COALESCE(si.cost_price, pv.cost_price))) AS total_cost
                   FROM (public.sale_items si
                     LEFT JOIN public.product_variants pv ON ((si.product_variant_id = pv.product_variant_id)))
                  GROUP BY si.sale_id) cogs ON ((s.sale_id = cogs.sale_id)))
        ), counterdaily AS (
         SELECT countersalesagg.sale_date,
            count(*) AS counter_sale_count,
            sum(
                CASE
                    WHEN ((countersalesagg.sale_status)::text = 'Completed'::text) THEN countersalesagg.total_amount
                    ELSE (0)::numeric
                END) AS counter_completed_revenue,
            sum(
                CASE
                    WHEN ((countersalesagg.sale_status)::text = 'Completed'::text) THEN countersalesagg.sale_cogs
                    ELSE (0)::numeric
                END) AS counter_completed_cogs
           FROM countersalesagg
          GROUP BY countersalesagg.sale_date
        ), repairticketsagg AS (
         SELECT r.repair_ticket_id,
            (COALESCE(r.delivered_at, r.completed_at, r.received_at))::date AS sale_date,
            r.ticket_status,
            r.final_cost,
            COALESCE(cogs.total_cost, (0)::numeric) AS repair_cogs
           FROM (public.repair_tickets r
             LEFT JOIN ( SELECT rti.repair_ticket_id,
                    sum(((rti.quantity)::numeric * COALESCE(pv.cost_price, (0)::numeric))) AS total_cost
                   FROM (public.repair_ticket_items rti
                     LEFT JOIN public.product_variants pv ON ((rti.product_variant_id = pv.product_variant_id)))
                  GROUP BY rti.repair_ticket_id) cogs ON ((r.repair_ticket_id = cogs.repair_ticket_id)))
        ), repairdaily AS (
         SELECT repairticketsagg.sale_date,
            count(*) AS repair_ticket_count,
            sum(
                CASE
                    WHEN ((repairticketsagg.ticket_status)::text = ANY (ARRAY['Completed'::character varying, 'Delivered'::character varying]::text[])) THEN repairticketsagg.final_cost
                    ELSE (0)::numeric
                END) AS repair_completed_revenue,
            sum(
                CASE
                    WHEN ((repairticketsagg.ticket_status)::text = ANY (ARRAY['Completed'::character varying, 'Delivered'::character varying]::text[])) THEN repairticketsagg.repair_cogs
                    ELSE (0)::numeric
                END) AS repair_completed_cogs
           FROM repairticketsagg
          GROUP BY repairticketsagg.sale_date
        ), expensedaily AS (
         SELECT (e.paid_at)::date AS sale_date,
            sum(e.amount) AS daily_expense_amount
           FROM public.shop_expenses e
          GROUP BY ((e.paid_at)::date)
        ), alldates AS (
         SELECT onlinedaily.sale_date
           FROM onlinedaily
        UNION
         SELECT counterdaily.sale_date
           FROM counterdaily
        UNION
         SELECT repairdaily.sale_date
           FROM repairdaily
        UNION
         SELECT expensedaily.sale_date
           FROM expensedaily
        )
 SELECT d.sale_date,
    COALESCE(od.online_order_count, (0)::bigint) AS online_order_count,
    COALESCE(od.online_cancelled_count, (0)::bigint) AS online_cancelled_count,
    COALESCE(cd.counter_sale_count, (0)::bigint) AS counter_sale_count,
    COALESCE(rd.repair_ticket_count, (0)::bigint) AS repair_ticket_count,
    COALESCE(od.online_completed_revenue, (0)::numeric) AS online_completed_revenue,
    COALESCE(cd.counter_completed_revenue, (0)::numeric) AS counter_completed_revenue,
    COALESCE(rd.repair_completed_revenue, (0)::numeric) AS repair_completed_revenue,
    ((COALESCE(od.online_completed_revenue, (0)::numeric) + COALESCE(cd.counter_completed_revenue, (0)::numeric)) + COALESCE(rd.repair_completed_revenue, (0)::numeric)) AS gross_revenue,
    COALESCE(od.online_completed_cogs, (0)::numeric) AS online_completed_cogs,
    COALESCE(cd.counter_completed_cogs, (0)::numeric) AS counter_completed_cogs,
    COALESCE(rd.repair_completed_cogs, (0)::numeric) AS repair_completed_cogs,
    ((COALESCE(od.online_completed_cogs, (0)::numeric) + COALESCE(cd.counter_completed_cogs, (0)::numeric)) + COALESCE(rd.repair_completed_cogs, (0)::numeric)) AS total_cogs,
    (((COALESCE(od.online_completed_revenue, (0)::numeric) + COALESCE(cd.counter_completed_revenue, (0)::numeric)) + COALESCE(rd.repair_completed_revenue, (0)::numeric)) - ((COALESCE(od.online_completed_cogs, (0)::numeric) + COALESCE(cd.counter_completed_cogs, (0)::numeric)) + COALESCE(rd.repair_completed_cogs, (0)::numeric))) AS gross_profit,
    COALESCE(ed.daily_expense_amount, (0)::numeric) AS daily_expense_amount,
    ((((COALESCE(od.online_completed_revenue, (0)::numeric) + COALESCE(cd.counter_completed_revenue, (0)::numeric)) + COALESCE(rd.repair_completed_revenue, (0)::numeric)) - ((COALESCE(od.online_completed_cogs, (0)::numeric) + COALESCE(cd.counter_completed_cogs, (0)::numeric)) + COALESCE(rd.repair_completed_cogs, (0)::numeric))) - COALESCE(ed.daily_expense_amount, (0)::numeric)) AS net_profit
   FROM ((((alldates d
     LEFT JOIN onlinedaily od ON ((d.sale_date = od.sale_date)))
     LEFT JOIN counterdaily cd ON ((d.sale_date = cd.sale_date)))
     LEFT JOIN repairdaily rd ON ((d.sale_date = rd.sale_date)))
     LEFT JOIN expensedaily ed ON ((d.sale_date = ed.sale_date)));


--
-- Name: vw_warranty_lookup; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_warranty_lookup AS
 SELECT 'Online'::text AS sale_channel,
    o.order_code AS invoice_code,
    o.recipient_name AS customer_name,
    o.recipient_phone AS customer_phone,
    oi.product_name,
    oi.variant_name,
    oi.sku,
    oi.warranty_months,
    COALESCE(s.delivered_at, o.completed_at) AS purchase_date,
        CASE
            WHEN (COALESCE(s.delivered_at, o.completed_at) IS NOT NULL) THEN (COALESCE(s.delivered_at, o.completed_at) + ((oi.warranty_months)::double precision * '1 mon'::interval))
            ELSE NULL::timestamp with time zone
        END AS warranty_expire_date,
        CASE
            WHEN (oi.warranty_months = 0) THEN 'Không bảo hành'::text
            WHEN ((COALESCE(s.delivered_at, o.completed_at) IS NULL) AND ((o.order_status)::text = ANY ((ARRAY['PendingConfirmation'::character varying, 'Confirmed'::character varying, 'Processing'::character varying, 'Shipped'::character varying])::text[]))) THEN 'Chờ giao hàng để kích hoạt'::text
            WHEN (COALESCE(s.delivered_at, o.completed_at) IS NULL) THEN 'Chưa kích hoạt'::text
            WHEN (CURRENT_TIMESTAMP <= (COALESCE(s.delivered_at, o.completed_at) + ((oi.warranty_months)::double precision * '1 mon'::interval))) THEN 'Còn hạn bảo hành'::text
            ELSE 'Hết hạn bảo hành'::text
        END AS warranty_status
   FROM ((public.orders o
     JOIN public.order_items oi ON ((o.order_id = oi.order_id)))
     LEFT JOIN public.shipments s ON (((o.order_id = s.order_id) AND ((s.shipment_status)::text = 'Delivered'::text))))
  WHERE ((o.order_status)::text <> 'Cancelled'::text)
UNION ALL
 SELECT 'Counter'::text AS sale_channel,
    s.sale_code AS invoice_code,
    COALESCE(s.customer_name, 'Khách lẻ'::character varying) AS customer_name,
    s.customer_phone,
    si.product_name,
    si.variant_name,
    si.sku,
    si.warranty_months,
    s.sold_at AS purchase_date,
    (s.sold_at + ((si.warranty_months)::double precision * '1 mon'::interval)) AS warranty_expire_date,
        CASE
            WHEN (si.warranty_months = 0) THEN 'Không bảo hành'::text
            WHEN (CURRENT_TIMESTAMP <= (s.sold_at + ((si.warranty_months)::double precision * '1 mon'::interval))) THEN 'Còn hạn bảo hành'::text
            ELSE 'Hết hạn bảo hành'::text
        END AS warranty_status
   FROM (public.sales s
     JOIN public.sale_items si ON ((s.sale_id = si.sale_id)))
  WHERE ((s.sale_status)::text = 'Completed'::text)
UNION ALL
 SELECT 'RepairService'::text AS sale_channel,
    rt.ticket_code AS invoice_code,
    rt.customer_name,
    rt.customer_phone,
    rt.device_model AS product_name,
    COALESCE(rt.solution_note, 'Sửa chữa / Recell'::character varying) AS variant_name,
    COALESCE(rt.serial_or_pin_code, 'N/A'::character varying) AS sku,
    rt.warranty_months,
    COALESCE(rt.delivered_at, rt.completed_at) AS purchase_date,
    (COALESCE(rt.delivered_at, rt.completed_at) + ((rt.warranty_months)::double precision * '1 mon'::interval)) AS warranty_expire_date,
        CASE
            WHEN (rt.warranty_months = 0) THEN 'Không bảo hành'::text
            WHEN (CURRENT_TIMESTAMP <= (COALESCE(rt.delivered_at, rt.completed_at) + ((rt.warranty_months)::double precision * '1 mon'::interval))) THEN 'Còn hạn bảo hành'::text
            ELSE 'Hết hạn bảo hành'::text
        END AS warranty_status
   FROM public.repair_tickets rt
  WHERE ((rt.ticket_status)::text = ANY (ARRAY['Completed'::character varying, 'Delivered'::character varying]::text[]));


--
-- Data for Name: addresses; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.addresses (address_id, user_id, recipient_name, phone, province, district, ward, street_address, is_default, created_at, updated_at) FROM stdin;
1	6	Nguyễn Văn An	0906789012	Hồ Chí Minh	Quận 7	Phường Tân Phong	120 Nguyễn Thị Thập	t	2026-11-03 07:53:47.620553+00	\N
2	7	Trần Bình	0907890123	Hồ Chí Minh	Bình Thạnh	Phường 25	45 Ung Văn Khiêm	t	2026-11-03 07:53:47.620553+00	\N
\.


--
-- Data for Name: assembly_order_items; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.assembly_order_items (assembly_order_item_id, assembly_order_id, component_variant_id, quantity_consumed, unit_cost) FROM stdin;
1	1	1	50	62000.00
2	1	4	5	75000.00
3	1	6	5	65000.00
4	1	8	5	25000.00
5	2	2	40	42000.00
6	2	4	4	75000.00
7	2	7	4	72000.00
8	2	8	4	25000.00
\.


--
-- Data for Name: assembly_orders; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.assembly_orders (assembly_order_id, assembly_code, bundle_variant_id, quantity_produced, total_cost, status, note, created_by_user_id, assembled_at) FROM stdin;
1	ASM-202610-001	9	5	3925000.00	Completed	Đóng 5 pack pin Makita 6Ah xuất bán và lưu kho	5	2026-10-18 08:00:00+00
2	ASM-202610-002	10	4	2228000.00	Completed	Đóng 4 pack pin DeWalt 5Ah xuất kho showroom	5	2026-10-19 09:30:00+00
\.


--
-- Data for Name: attribute_values; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.attribute_values (attribute_value_id, attribute_id, value_name, created_at) FROM stdin;
1	1	Makita M21 (Chân phổ thông 4cm)	2026-11-03 07:53:47.632585+00
2	1	DeWalt 20V Max	2026-11-03 07:53:47.632585+00
3	1	Milwaukee M18	2026-11-03 07:53:47.632585+00
4	2	18V - 21V (5S)	2026-11-03 07:53:47.632585+00
5	2	36V (10S)	2026-11-03 07:53:47.632585+00
6	2	72V (20S)	2026-11-03 07:53:47.632585+00
7	3	Li-ion 18650	2026-11-03 07:53:47.632585+00
8	3	Li-ion 21700	2026-11-03 07:53:47.632585+00
\.


--
-- Data for Name: attributes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.attributes (attribute_id, attribute_name, attribute_code, created_at) FROM stdin;
1	Chuẩn Chân Cắm	SOURCE_PIN	2026-11-03 07:53:47.631437+00
2	Điện Áp Hoạt Động	VOLTAGE	2026-11-03 07:53:47.631437+00
3	Quy Cách Cell	CELL_TYPE	2026-11-03 07:53:47.631437+00
\.


--
-- Data for Name: categories; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.categories (category_id, parent_category_id, category_name, slug, description, is_active, display_order, created_at, updated_at) FROM stdin;
1	\N	Cell Pin Lithium	cell-pin-lithium	Các loại cell pin 18650, 21700, 32700 xả cao chuyên dụng	t	1	2026-11-03 07:53:47.624492+00	\N
2	\N	Mạch Bảo Vệ BMS	mach-bao-ve-bms	Mạch bảo vệ sạc xả pin công cụ, xe điện, năng lượng mặt trời	t	2	2026-11-03 07:53:47.624492+00	\N
3	\N	Vỏ Hộp & Phụ Kiện Gia Công	vo-hop-phu-kien	Vỏ pin máy công cụ, khung giữ cell, kẽm hàn pin mạ niken	t	3	2026-11-03 07:53:47.624492+00	\N
4	\N	Khối Pin Đóng Hoàn Thiện	khoi-pin-dong-hoan-thien	Khối pin đã đóng hoàn thiện theo công thức BOM tiêu chuẩn	t	4	2026-11-03 07:53:47.624492+00	\N
\.


--
-- Data for Name: customers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.customers (customer_id, phone, full_name, user_id, total_spent, created_at, updated_at) FROM stdin;
1	0901234567	Chủ Shop Pin Tuấn Hùng	1	0.00	2026-11-03 07:53:47.613752+00	\N
2	0902345678	Quản Lý Lê Văn Minh	2	0.00	2026-11-03 07:53:47.613752+00	\N
3	0903456789	Thủ Kho Nguyễn Văn Hùng	3	0.00	2026-11-03 07:53:47.613752+00	\N
4	0904567890	Thu Ngân Hoàng Thị Mai	4	0.00	2026-11-03 07:53:47.613752+00	\N
5	0905678901	Kỹ Thuật Viên Trần Quốc Đạt	5	0.00	2026-11-03 07:53:47.613752+00	\N
489	099115391	E2E Test User	500	0.00	2026-11-03 11:48:03.075378+00	\N
6	0906789012	Nguyễn Văn An	6	970000.00	2026-11-03 07:53:47.613752+00	\N
7	0907890123	Trần Bình	7	380000.00	2026-11-03 07:53:47.613752+00	\N
490	09888589	<script>alert('XSS')</script>	501	0.00	2026-11-03 11:48:03.77477+00	\N
491	091117082	Nguyen Journey Customer	502	0.00	2026-11-03 11:48:04.846963+00	\N
492	099118233	E2E Test User	503	0.00	2026-11-03 11:49:45.343551+00	\N
493	098832189	<script>alert('XSS')</script>	504	0.00	2026-11-03 11:49:45.846448+00	\N
368	0965880988	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 11:31:11.49017+00	\N
430	0962761504	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 11:42:44.61427+00	\N
131	0965089957	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:28:22.694377+00	\N
494	091123142	Nguyen Journey Customer	505	0.00	2026-11-03 11:49:46.554411+00	\N
495	099110080	E2E Test User	506	0.00	2026-11-03 11:51:08.16076+00	\N
496	0988140	<script>alert('XSS')</script>	507	0.00	2026-11-03 11:51:08.839498+00	\N
278	0965756751	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:35:00.055324+00	\N
497	091124413	Nguyen Journey Customer	508	0.00	2026-11-03 11:51:09.927239+00	\N
134	0953141990	Security Test User	\N	0.00	2026-11-03 10:28:22.758749+00	\N
372	0955002958	Security Test User	\N	0.00	2026-11-03 11:31:11.55508+00	\N
498	099129346	E2E Test User	509	0.00	2026-11-03 11:51:40.733765+00	\N
499	098813923	<script>alert('XSS')</script>	510	0.00	2026-11-03 11:51:41.229659+00	\N
433	0953032745	Security Test User	\N	0.00	2026-11-03 11:42:44.679216+00	\N
282	0951619680	Security Test User	\N	0.00	2026-11-03 10:35:00.120817+00	\N
500	091119910	Nguyen Journey Customer	511	0.00	2026-11-03 11:51:41.963087+00	\N
8	0908999888	Nguyễn Thị Hoa	\N	265000.00	2026-11-03 07:53:47.621765+00	2026-11-08 11:50:14.83186+00
503	0961656239	Tài Khoản Bị Khóa	\N	0.00	2026-11-04 04:24:47.974687+00	\N
713	0968107920	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:15:35.71032+00	\N
563	0969549162	Tài Khoản Bị Khóa	\N	0.00	2026-11-04 04:32:32.923777+00	\N
653	0964357096	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:01:58.021086+00	\N
506	0956482872	Security Test User	\N	0.00	2026-11-04 04:24:48.070352+00	\N
153	0942950787	Cookie Test User	\N	0.00	2026-11-03 10:28:23.513704+00	\N
566	0959292105	Security Test User	\N	0.00	2026-11-04 04:32:32.992564+00	\N
657	0953442443	Security Test User	\N	0.00	2026-11-08 11:01:58.080083+00	\N
716	0954140874	Security Test User	\N	0.00	2026-11-08 11:15:35.770742+00	\N
157	0964918510	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:28:27.407205+00	\N
395	0941526147	Cookie Test User	\N	0.00	2026-11-03 11:31:12.189072+00	\N
161	0954420298	Security Test User	\N	0.00	2026-11-03 10:28:27.487547+00	\N
305	0943382658	Cookie Test User	\N	0.00	2026-11-03 10:35:00.826608+00	\N
455	0949874707	Cookie Test User	\N	0.00	2026-11-03 11:42:45.350769+00	\N
234	0967543337	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:31:17.221599+00	\N
456	099122241	E2E Test User	466	0.00	2026-11-03 11:43:44.236871+00	\N
235	0959868808	Security Test User	\N	0.00	2026-11-03 10:31:17.318364+00	\N
457	098814498	<script>alert('XSS')</script>	467	0.00	2026-11-03 11:43:44.872064+00	\N
308	0964492528	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 11:24:30.218727+00	\N
398	0968933679	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 11:37:05.224217+00	\N
458	091107402	Nguyen Journey Customer	468	0.00	2026-11-03 11:43:45.894035+00	\N
311	0952788146	Security Test User	\N	0.00	2026-11-03 11:24:30.28179+00	\N
402	0955793370	Security Test User	\N	0.00	2026-11-03 11:37:05.284868+00	\N
238	0941120154	Cookie Test User	\N	0.00	2026-11-03 10:31:17.898761+00	\N
178	0943851045	Cookie Test User	\N	0.00	2026-11-03 10:28:28.29707+00	\N
128	0912345678	Full Name	\N	0.00	2026-11-03 10:28:22.419413+00	2026-11-03 10:31:16.974014+00
179	0969731796	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:28:31.583005+00	\N
180	0955177546	Security Test User	\N	0.00	2026-11-03 10:28:31.730844+00	\N
239	0962125635	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:32:17.803744+00	\N
240	0958760669	Security Test User	\N	0.00	2026-11-03 10:32:17.925161+00	\N
461	0962190134	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 11:46:02.450813+00	\N
183	0943276034	Cookie Test User	\N	0.00	2026-11-03 10:28:32.357783+00	\N
464	0955642829	Security Test User	\N	0.00	2026-11-03 11:46:02.512168+00	\N
243	0948531934	Cookie Test User	\N	0.00	2026-11-03 10:32:18.500646+00	\N
530	0949877023	Cookie Test User	\N	0.00	2026-11-04 04:24:48.91346+00	\N
246	0967426232	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 10:32:27.554972+00	\N
590	0944477037	Cookie Test User	\N	0.00	2026-11-04 04:32:33.83197+00	\N
533	0966087246	Tài Khoản Bị Khóa	\N	0.00	2026-11-04 04:26:29.472478+00	\N
250	0955060372	Security Test User	\N	0.00	2026-11-03 10:32:27.609671+00	\N
680	0945213353	Cookie Test User	\N	0.00	2026-11-08 11:01:58.799586+00	\N
593	0968988181	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:00:57.753187+00	\N
536	0951148932	Security Test User	\N	0.00	2026-11-04 04:26:29.542924+00	\N
740	0948335784	Cookie Test User	\N	0.00	2026-11-08 11:15:36.415541+00	\N
335	0941528263	Cookie Test User	\N	0.00	2026-11-03 11:24:30.931401+00	\N
425	0944604040	Cookie Test User	\N	0.00	2026-11-03 11:37:05.938655+00	\N
683	0969953578	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:12:49.51233+00	\N
596	0956306356	Security Test User	\N	0.00	2026-11-08 11:00:57.830303+00	\N
338	0962384757	Tài Khoản Bị Khóa	\N	0.00	2026-11-03 11:24:53.145413+00	\N
686	0951240735	Security Test User	\N	0.00	2026-11-08 11:12:49.568833+00	\N
342	0959657632	Security Test User	\N	0.00	2026-11-03 11:24:53.230249+00	\N
488	0942795863	Cookie Test User	\N	0.00	2026-11-03 11:46:03.147626+00	\N
273	0941160779	Cookie Test User	\N	0.00	2026-11-03 10:32:28.308341+00	\N
560	0949046540	Cookie Test User	\N	0.00	2026-11-04 04:26:30.292361+00	\N
766	0967215104	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:17:11.445017+00	\N
620	0941833346	Cookie Test User	\N	0.00	2026-11-08 11:00:58.852736+00	\N
365	0949962375	Cookie Test User	\N	0.00	2026-11-03 11:24:53.895414+00	\N
767	0955461261	Security Test User	\N	0.00	2026-11-08 11:17:11.486532+00	\N
623	0967859510	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:01:00.210074+00	\N
710	0944935570	Cookie Test User	\N	0.00	2026-11-08 11:12:50.185392+00	\N
627	0952386622	Security Test User	\N	0.00	2026-11-08 11:01:00.285866+00	\N
770	0941090562	Cookie Test User	\N	0.00	2026-11-08 11:17:12.056007+00	\N
650	0942240613	Cookie Test User	\N	0.00	2026-11-08 11:01:01.261827+00	\N
771	0967219588	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:18:56.776588+00	\N
831	0963223371	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:22:14.951124+00	\N
772	0954857570	Security Test User	\N	0.00	2026-11-08 11:18:56.893213+00	\N
1421	0967391466	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:44:37.185043+00	\N
832	0951776230	Security Test User	\N	0.00	2026-11-08 11:22:15.0722+00	\N
1511	0963770677	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:50:14.523538+00	\N
1422	0955798556	Security Test User	\N	0.00	2026-11-08 11:44:37.317565+00	\N
1512	0951443290	Security Test User	\N	0.00	2026-11-08 11:50:14.648186+00	\N
775	0943802592	Cookie Test User	\N	0.00	2026-11-08 11:18:57.489304+00	\N
835	0941524423	Cookie Test User	\N	0.00	2026-11-08 11:22:15.691263+00	\N
1425	0949679244	Cookie Test User	\N	0.00	2026-11-08 11:44:37.924063+00	\N
936	0964334302	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:23:52.395775+00	\N
1146	0962892056	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:26:43.184388+00	\N
937	0959037902	Security Test User	\N	0.00	2026-11-08 11:23:52.445616+00	\N
1236	0966539611	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:35:59.871975+00	\N
1147	0952227730	Security Test User	\N	0.00	2026-11-08 11:26:43.226853+00	\N
1326	0967326689	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:41:53.834834+00	\N
1237	0952656865	Security Test User	\N	0.00	2026-11-08 11:35:59.913418+00	\N
1327	0959596140	Security Test User	\N	0.00	2026-11-08 11:41:53.881029+00	\N
940	0947142501	Cookie Test User	\N	0.00	2026-11-08 11:23:53.008311+00	\N
1515	0947407802	Cookie Test User	\N	0.00	2026-11-08 11:50:15.238105+00	\N
1150	0941148174	Cookie Test User	\N	0.00	2026-11-08 11:26:43.794403+00	\N
1240	0946383475	Cookie Test User	\N	0.00	2026-11-08 11:36:00.484451+00	\N
1330	0943957508	Cookie Test User	\N	0.00	2026-11-08 11:41:54.484684+00	\N
1331	0966380544	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:41:54.819747+00	\N
951	0964281098	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:25:21.299437+00	\N
952	0953284400	Security Test User	\N	0.00	2026-11-08 11:25:21.477887+00	\N
1332	0952082586	Security Test User	\N	0.00	2026-11-08 11:41:54.949472+00	\N
955	0947751426	Cookie Test User	\N	0.00	2026-11-08 11:25:22.245987+00	\N
1335	0947780686	Cookie Test User	\N	0.00	2026-11-08 11:41:55.556959+00	\N
1256	0966023952	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:38:31.627654+00	\N
958	0965993926	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:25:31.545086+00	\N
1257	0954913747	Security Test User	\N	0.00	2026-11-08 11:38:31.67373+00	\N
964	0955296235	Security Test User	\N	0.00	2026-11-08 11:25:31.697307+00	\N
826	0966941303	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:19:41.177854+00	\N
1176	0967075236	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:26:51.721063+00	\N
827	0953139823	Security Test User	\N	0.00	2026-11-08 11:19:41.230081+00	\N
1177	0959661985	Security Test User	\N	0.00	2026-11-08 11:26:51.772099+00	\N
886	0961436711	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:22:52.168965+00	\N
887	0953104487	Security Test User	\N	0.00	2026-11-08 11:22:52.212664+00	\N
1260	0945449858	Cookie Test User	\N	0.00	2026-11-08 11:38:32.252962+00	\N
830	0948612771	Cookie Test User	\N	0.00	2026-11-08 11:19:41.787413+00	\N
1180	0947582060	Cookie Test User	\N	0.00	2026-11-08 11:26:52.51478+00	\N
890	0941314292	Cookie Test User	\N	0.00	2026-11-08 11:22:52.797693+00	\N
1181	0964310249	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:30:34.03328+00	\N
1182	0956190924	Security Test User	\N	0.00	2026-11-08 11:30:34.154147+00	\N
1185	0949509743	Cookie Test User	\N	0.00	2026-11-08 11:30:34.733244+00	\N
984	0949596749	Cookie Test User	\N	0.00	2026-11-08 11:25:32.423351+00	\N
1466	0961026499	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:45:15.498253+00	\N
1467	0953783122	Security Test User	\N	0.00	2026-11-08 11:45:15.54214+00	\N
1281	0968663236	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:41:01.280568+00	\N
1282	0952329315	Security Test User	\N	0.00	2026-11-08 11:41:01.322713+00	\N
916	0967445290	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:23:12.874866+00	\N
917	0951653921	Security Test User	\N	0.00	2026-11-08 11:23:12.920533+00	\N
1470	0947790993	Cookie Test User	\N	0.00	2026-11-08 11:45:16.13217+00	\N
1371	0963142844	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:43:10.168495+00	\N
1285	0943054325	Cookie Test User	\N	0.00	2026-11-08 11:41:01.872656+00	\N
1372	0959164304	Security Test User	\N	0.00	2026-11-08 11:43:10.216198+00	\N
920	0945652665	Cookie Test User	\N	0.00	2026-11-08 11:23:13.496188+00	\N
1375	0947255534	Cookie Test User	\N	0.00	2026-11-08 11:43:10.822795+00	\N
1021	0969439471	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:25:37.470689+00	\N
1022	0958629342	Security Test User	\N	0.00	2026-11-08 11:25:37.516341+00	\N
1386	0967036170	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:43:17.783129+00	\N
1387	0959692257	Security Test User	\N	0.00	2026-11-08 11:43:17.828925+00	\N
1025	0945538093	Cookie Test User	\N	0.00	2026-11-08 11:25:38.12685+00	\N
1506	0964967660	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:46:00.679657+00	\N
1041	0968631443	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:25:48.240962+00	\N
1507	0958588065	Security Test User	\N	0.00	2026-11-08 11:46:00.721352+00	\N
1042	0957369899	Security Test User	\N	0.00	2026-11-08 11:25:48.382854+00	\N
1405	0946817069	Cookie Test User	\N	0.00	2026-11-08 11:43:18.430326+00	\N
1045	0948031173	Cookie Test User	\N	0.00	2026-11-08 11:25:49.21988+00	\N
1510	0941337709	Cookie Test User	\N	0.00	2026-11-08 11:46:01.268449+00	\N
1071	0968713381	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:25:53.283049+00	\N
1072	0956718817	Security Test User	\N	0.00	2026-11-08 11:25:53.43161+00	\N
1075	0946654679	Cookie Test User	\N	0.00	2026-11-08 11:25:54.144583+00	\N
1116	0968557996	Tài Khoản Bị Khóa	\N	0.00	2026-11-08 11:26:28.083592+00	\N
1117	0952420521	Security Test User	\N	0.00	2026-11-08 11:26:28.125061+00	\N
1120	0949899781	Cookie Test User	\N	0.00	2026-11-08 11:26:28.671557+00	\N
\.


--
-- Data for Name: inventory; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.inventory (product_variant_id, quantity_on_hand, quantity_reserved, reorder_level, updated_at, version) FROM stdin;
3	100	0	20	2026-11-03 07:53:47.671313+00	1
4	91	0	20	2026-11-03 07:53:47.671313+00	1
5	20	0	5	2026-11-03 07:53:47.671313+00	1
6	94	0	20	2026-11-03 07:53:47.671313+00	1
7	76	0	20	2026-11-03 07:53:47.671313+00	1
8	90	0	20	2026-11-03 07:53:47.671313+00	1
9	4	0	5	2026-11-03 07:53:47.671313+00	1
10	4	0	5	2026-11-03 07:53:47.671313+00	1
1	470	0	50	2026-11-03 11:51:42.230693+00	1
2	255	0	50	2026-11-08 11:50:14.219846+00	1
\.


--
-- Data for Name: inventory_transactions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.inventory_transactions (inventory_transaction_id, product_variant_id, transaction_type, quantity_change, reference_type, reference_id, unit_cost, note, created_by_user_id, created_at) FROM stdin;
1	1	PurchaseReceipt	500	purchase_receipts	1	62000.00	Nhập kho phiếu PNK-202610-001	3	2026-10-10 02:15:00+00
2	2	PurchaseReceipt	300	purchase_receipts	1	42000.00	Nhập kho phiếu PNK-202610-001	3	2026-10-10 02:15:00+00
3	3	PurchaseReceipt	100	purchase_receipts	1	98000.00	Nhập kho phiếu PNK-202610-001	3	2026-10-10 02:15:00+00
4	4	PurchaseReceipt	100	purchase_receipts	2	75000.00	Nhập kho phiếu PNK-202610-002	3	2026-10-12 03:45:00+00
5	5	PurchaseReceipt	20	purchase_receipts	2	1100000.00	Nhập kho phiếu PNK-202610-002	3	2026-10-12 03:45:00+00
6	6	PurchaseReceipt	100	purchase_receipts	3	65000.00	Nhập kho phiếu PNK-202610-003	3	2026-10-14 07:15:00+00
7	7	PurchaseReceipt	80	purchase_receipts	3	72000.00	Nhập kho phiếu PNK-202610-003	3	2026-10-14 07:15:00+00
8	8	PurchaseReceipt	100	purchase_receipts	3	25000.00	Nhập kho phiếu PNK-202610-003	3	2026-10-14 07:15:00+00
9	1	AssemblyConsumption	-50	assembly_orders	1	62000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-001	5	2026-10-18 08:05:00+00
10	4	AssemblyConsumption	-5	assembly_orders	1	75000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-001	5	2026-10-18 08:05:00+00
11	6	AssemblyConsumption	-5	assembly_orders	1	65000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-001	5	2026-10-18 08:05:00+00
12	8	AssemblyConsumption	-5	assembly_orders	1	25000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-001	5	2026-10-18 08:05:00+00
13	9	AssemblyProduction	5	assembly_orders	1	785000.00	Nhập kho thành phẩm lệnh ASM-202610-001	5	2026-10-18 08:10:00+00
14	2	AssemblyConsumption	-40	assembly_orders	2	42000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-002	5	2026-10-19 09:35:00+00
15	4	AssemblyConsumption	-4	assembly_orders	2	75000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-002	5	2026-10-19 09:35:00+00
16	7	AssemblyConsumption	-4	assembly_orders	2	72000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-002	5	2026-10-19 09:35:00+00
17	8	AssemblyConsumption	-4	assembly_orders	2	25000.00	Xuất linh kiện đóng khối pin lệnh ASM-202610-002	5	2026-10-19 09:35:00+00
18	10	AssemblyProduction	4	assembly_orders	2	557000.00	Nhập kho thành phẩm lệnh ASM-202610-002	5	2026-10-19 09:40:00+00
19	9	OnlineSale	-1	orders	1	785000.00	Xuất kho giao đơn hàng trực tuyến ORD-202610-001	3	2026-10-22 08:10:00+00
20	8	OnlineSale	-1	orders	1	25000.00	Xuất kho giao đơn hàng trực tuyến ORD-202610-001	3	2026-10-22 08:10:00+00
21	1	CounterSale	-2	sales	1	62000.00	Xuất bán lẻ tại quầy POS-202610-001	4	2026-10-25 04:25:00+00
22	6	CounterSale	-1	sales	1	65000.00	Xuất bán lẻ tại quầy POS-202610-001	4	2026-10-25 04:25:00+00
23	2	RepairConsumption	-5	repair_tickets	1	42000.00	Xuất linh kiện sửa chữa phiếu RPR-202610-001	5	2026-10-21 03:00:00+00
24	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:43:45.447717+00
25	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:43:45.511307+00
26	1	CounterSale	-1	Sale	0	62000.00	Sold at counter POS-20261103114345-1	1	2026-11-03 11:43:45.575192+00
28	1	StockAudit	5	ManualAdjustment	0	85000.00	Periodic cycle count reconciliation	1	2026-11-03 11:43:46.150922+00
29	1	RepairConsumption	-1	RepairTicket	4	62000.00	Parts used in repair ticket REP-20261103114346-1	1	2026-11-03 11:43:46.253973+00
30	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:48:04.369778+00
31	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:48:04.437391+00
32	1	CounterSale	-1	Sale	0	62000.00	Sold at counter POS-20261103114804-1	1	2026-11-03 11:48:04.505321+00
34	1	StockAudit	5	ManualAdjustment	0	85000.00	Periodic cycle count reconciliation	1	2026-11-03 11:48:05.11581+00
35	1	RepairConsumption	-1	RepairTicket	7	62000.00	Parts used in repair ticket REP-20261103114805-1	1	2026-11-03 11:48:05.222745+00
36	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:49:46.29812+00
37	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:49:46.322457+00
38	1	CounterSale	-1	Sale	0	62000.00	Sold at counter POS-20261103114946-1	1	2026-11-03 11:49:46.364354+00
40	1	StockAudit	5	ManualAdjustment	0	85000.00	Periodic cycle count reconciliation	1	2026-11-03 11:49:46.741638+00
41	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:51:09.445108+00
42	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:51:09.512953+00
43	1	CounterSale	-1	Sale	0	62000.00	Sold at counter POS-20261103115109-1	1	2026-11-03 11:51:09.578921+00
45	1	StockAudit	5	ManualAdjustment	0	85000.00	Periodic cycle count reconciliation	1	2026-11-03 11:51:10.201992+00
46	1	RepairConsumption	-1	RepairTicket	13	62000.00	Parts used in repair ticket REP-20261103115110-1	1	2026-11-03 11:51:10.309045+00
47	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:51:41.708061+00
48	1	StockAudit	1	ManualAdjustment	0	0.00	E2E RBAC Matrix Test	1	2026-11-03 11:51:41.727095+00
49	1	CounterSale	-1	Sale	0	62000.00	Sold at counter POS-20261103115141-1	1	2026-11-03 11:51:41.769776+00
51	1	OnlineSale	-4	Order	2	62000.00	Delivered order ORD-202610-002	6	2026-11-03 11:51:42.091813+00
52	1	StockAudit	5	ManualAdjustment	0	85000.00	Periodic cycle count reconciliation	1	2026-11-03 11:51:42.149952+00
53	1	RepairConsumption	-1	RepairTicket	16	62000.00	Parts used in repair ticket REP-20261103115142-1	1	2026-11-03 11:51:42.230707+00
\.


--
-- Data for Name: order_items; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.order_items (order_item_id, order_id, product_variant_id, product_name, variant_name, sku, unit_price, cost_price, quantity, warranty_months) FROM stdin;
1	1	9	Khối Pin Đóng Sẵn Makita 18V 6.0Ah Chân Phổ Thông	Khối Pin Makita 18V 6.0Ah VTC6	PACK-MAKITA-6AH	950000.00	785000.00	1	12
2	1	8	Cuộn Kẽm Hàn Pin Mạ Niken 0.15mm x 8mm 10m	Cuộn Kẽm Hàn 0.15mm x 8mm 10m	NICKEL-015	45000.00	25000.00	1	0
3	2	1	Cell Pin Sony Murata US18650-VTC6 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	4	6
\.


--
-- Data for Name: orders; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.orders (order_id, order_code, user_id, recipient_name, recipient_phone, shipping_address, shipping_province, shipping_district, shipping_ward, shipping_method_id, voucher_id, subtotal_amount, discount_amount, shipping_fee, total_amount, order_status, payment_status, customer_note, staff_note, confirmed_by_user_id, placed_at, confirmed_at, completed_at, cancelled_at, admin_note, payment_method, subtotal) FROM stdin;
1	ORD-202610-001	6	Nguyễn Văn An	0906789012	120 Nguyễn Thị Thập	Hồ Chí Minh	Quận 7	Phường Tân Phong	1	1	995000.00	50000.00	25000.00	970000.00	Completed	Paid	Đóng gói kỹ pin giúp mình nhé shop	\N	2	2026-10-22 03:00:00+00	2026-10-22 03:30:00+00	2026-10-24 09:00:00+00	\N	\N	COD	995000.00
2	ORD-202610-002	6	Nguyễn Văn An	0906789012	120 Nguyễn Thị Thập	Hồ Chí Minh	Quận 7	Phường Tân Phong	1	\N	340000.00	0.00	25000.00	365000.00	Completed	Paid	Giao giờ hành chính	\N	1	2026-10-28 07:00:00+00	2026-11-03 11:51:41.581471+00	2026-11-03 11:51:42.090397+00	\N	\N	COD	340000.00
\.


--
-- Data for Name: payments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.payments (payment_id, order_id, payment_method, transaction_code, amount, payment_status, paid_at, created_at, provider) FROM stdin;
1	1	VNPay	VNPAY-20261022-998877	970000.00	Success	2026-10-22 03:15:00+00	2026-10-22 03:00:00+00	\N
2	2	COD	\N	365000.00	Pending	\N	2026-10-28 07:00:00+00	\N
\.


--
-- Data for Name: product_images; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.product_images (image_id, product_id, image_url, is_thumbnail, display_order, created_at) FROM stdin;
1	1	https://images.batterypartsstore.vn/products/vtc6-front.jpg	t	1	2026-11-03 07:53:47.630017+00
2	2	https://images.batterypartsstore.vn/products/sam25r-front.jpg	t	1	2026-11-03 07:53:47.630017+00
3	3	https://images.batterypartsstore.vn/products/moli-p42a.jpg	t	1	2026-11-03 07:53:47.630017+00
4	4	https://images.batterypartsstore.vn/products/daly-5s-40a.jpg	t	1	2026-11-03 07:53:47.630017+00
5	5	https://images.batterypartsstore.vn/products/smart-bms-jk.jpg	t	1	2026-11-03 07:53:47.630017+00
6	6	https://images.batterypartsstore.vn/products/case-makita-10c.jpg	t	1	2026-11-03 07:53:47.630017+00
7	7	https://images.batterypartsstore.vn/products/case-dewalt-10c.jpg	t	1	2026-11-03 07:53:47.630017+00
8	8	https://images.batterypartsstore.vn/products/kem-han-015.jpg	t	1	2026-11-03 07:53:47.630017+00
9	9	https://images.batterypartsstore.vn/products/pack-makita-6ah.jpg	t	1	2026-11-03 07:53:47.630017+00
10	10	https://images.batterypartsstore.vn/products/pack-dewalt-5ah.jpg	t	1	2026-11-03 07:53:47.630017+00
\.


--
-- Data for Name: product_variant_attributes; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.product_variant_attributes (product_variant_id, attribute_value_id) FROM stdin;
1	7
2	7
3	8
4	4
6	1
7	2
9	1
9	4
9	7
10	2
10	4
10	7
\.


--
-- Data for Name: product_variant_bundles; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.product_variant_bundles (bundle_variant_id, component_variant_id, quantity) FROM stdin;
9	1	10
9	4	1
9	6	1
9	8	1
10	2	10
10	4	1
10	7	1
10	8	1
\.


--
-- Data for Name: product_variants; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.product_variants (product_variant_id, product_id, sku, variant_name, price, cost_price, weight_grams, warranty_months, is_bundle, is_active, created_at, updated_at, option_summary, unit, image_url) FROM stdin;
1	1	CELL-VTC6	Sony VTC6 18650 3000mAh 30A	85000.00	62000.00	48	6	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
2	2	CELL-SAM25R	Samsung 25R 18650 2500mAh 20A	60000.00	42000.00	46	6	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
3	3	CELL-MOLI-P42A	Molicel P42A 21700 4200mAh 45A	135000.00	98000.00	70	6	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
4	4	BMS-5S-40A	Daly BMS 5S 21V 40A Li-ion	110000.00	75000.00	60	6	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
5	5	BMS-JK-24S-100A	Smart BMS JK 8S-24S 100A Bluetooth	1450000.00	1100000.00	350	12	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
6	6	CASE-MAKITA-10C	Vỏ Pin Makita 18V 10 Cell Chân M21	95000.00	65000.00	180	3	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
7	7	CASE-DEWALT-10C	Vỏ Pin DeWalt 20V Max 10 Cell	105000.00	72000.00	190	3	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
8	8	NICKEL-015	Cuộn Kẽm Hàn 0.15mm x 8mm 10m	45000.00	25000.00	100	0	f	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
9	9	PACK-MAKITA-6AH	Khối Pin Makita 18V 6.0Ah VTC6	950000.00	785000.00	680	12	t	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
10	10	PACK-DEWALT-5AH	Khối Pin DeWalt 20V 5.0Ah Samsung 25R	820000.00	557000.00	650	12	t	t	2026-11-03 07:53:47.628076+00	\N	\N	Cái	\N
\.


--
-- Data for Name: products; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.products (product_id, category_id, product_name, slug, description, is_active, created_at, updated_at, brand, short_description, is_featured) FROM stdin;
1	1	Cell Pin Sony Murata US18650-VTC6 3000mAh 30A	cell-pin-sony-vtc6	Cell pin 18650 chính hãng Sony Murata tải xả liên tục 30A cực khỏe	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
2	1	Cell Pin Samsung INR18650-25R 2500mAh 20A	cell-pin-samsung-25r	Cell pin Samsung 25R xả 20A quốc dân chuyên dùng máy khoan, máy mài	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
3	1	Cell Pin Molicel INR-21700-P42A 4200mAh 45A	cell-pin-molicel-p42a	Cell pin 21700 Molicel dòng xả khủng 45A cho thiết bị công suất cao	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
4	2	Mạch Bảo Vệ Daly BMS 5S 21V 40A Tải Xả Cao	mach-daly-bms-5s-40a	Mạch BMS Daly chính hãng 5S 21V có IC cân bằng và cảm biến nhiệt	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
5	2	Mạch Smart BMS JK 8S-24S 100A Cân Bằng Chủ Động	mach-smart-bms-jk-24s	Mạch JK Smart BMS có Bluetooth quản lý qua App điện thoại	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
6	3	Vỏ Pin Makita 18V 10 Cell Kèm Mạch Báo Pin	vo-pin-makita-18v-10c	Vỏ pin chân phổ thông Makita M21 chứa 10 cell 18650 kèm đèn báo pin	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
7	3	Vỏ Pin DeWalt 20V Max 10 Cell Kèm Khung	vo-pin-dewalt-20v-10c	Vỏ pin DeWalt 10 cell chống va đập, chân tiếp xúc đồng mạ niken	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
8	3	Cuộn Kẽm Hàn Pin Mạ Niken 0.15mm x 8mm 10m	kem-han-pin-015	Kẽm cuộn hàn điểm chuyên dụng dẫn dòng tốt, không rỉ sét	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
9	4	Khối Pin Đóng Sẵn Makita 18V 6.0Ah Chân Phổ Thông	khoi-pin-makita-18v-6ah	Khối pin 5S2P 10 cell Sony VTC6 hoàn chỉnh kèm vỏ và mạch bảo vệ	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
10	4	Khối Pin Đóng Sẵn DeWalt 20V 5.0Ah	khoi-pin-dewalt-20v-5ah	Khối pin 5S2P 10 cell Samsung 25R hoàn chỉnh cho máy khoan DeWalt	t	2026-11-03 07:53:47.625741+00	\N	\N	\N	f
\.


--
-- Data for Name: purchase_receipt_items; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.purchase_receipt_items (purchase_receipt_item_id, purchase_receipt_id, product_variant_id, quantity, unit_cost) FROM stdin;
1	1	1	500	62000.00
2	1	2	300	42000.00
3	1	3	100	98000.00
4	2	4	100	75000.00
5	2	5	20	1100000.00
6	3	6	100	65000.00
7	3	7	80	72000.00
8	3	8	100	25000.00
\.


--
-- Data for Name: purchase_receipts; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.purchase_receipts (purchase_receipt_id, receipt_code, supplier_id, received_by_user_id, receipt_status, payment_status, total_amount, paid_amount, note, receipt_date, created_at, updated_at) FROM stdin;
3	PNK-202610-003	3	3	Completed	Unpaid	14760000.00	0.00	Nhập khuôn vỏ pin Makita, DeWalt và cuộn kẽm hàn	2026-10-14 07:00:00+00	2026-11-03 07:53:47.640847+00	\N
1	PNK-202610-001	1	3	Completed	Paid	53400000.00	53400000.00	Nhập cell pin Sony VTC6, Samsung 25R và Molicel đợt 1	2026-10-10 02:00:00+00	2026-11-03 07:53:47.640847+00	2026-11-03 07:53:47.644339+00
2	PNK-202610-002	2	3	Completed	PartiallyPaid	29500000.00	20000000.00	Nhập mạch Daly BMS 5S và Smart BMS JK	2026-10-12 03:30:00+00	2026-11-03 07:53:47.640847+00	2026-11-03 07:53:47.644339+00
\.


--
-- Data for Name: repair_ticket_items; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.repair_ticket_items (repair_ticket_item_id, repair_ticket_id, product_variant_id, quantity, unit_price) FROM stdin;
1	1	2	5	60000.00
2	4	1	1	85000.00
3	7	1	1	85000.00
4	13	1	1	85000.00
5	16	1	1	85000.00
\.


--
-- Data for Name: repair_tickets; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.repair_tickets (repair_ticket_id, ticket_code, customer_id, customer_name, customer_phone, device_model, serial_or_pin_code, issue_description, solution_note, estimated_cost, final_cost, warranty_months, ticket_status, payment_status, payment_method, created_by_user_id, technician_user_id, received_at, completed_at, delivered_at, cancelled_at, paid_amount) FROM stdin;
1	RPR-202610-001	7	Trần Bình	0907890123	Máy khoan pin DeWalt DCD996	DW-996-887766	Khối pin 20V không sạc được, báo lỗi nhấp nháy đèn đỏ	Thay thế 5 cell Samsung 25R và cân bằng áp các nhánh cell	400000.00	380000.00	3	Completed	Paid	Cash	4	5	2026-10-20 02:30:00+00	2026-10-21 09:00:00+00	2026-10-22 07:00:00+00	\N	0.00
2	REP-20261103114345-1	\N	Nguyen Van Test	0911223344	E-Bike Battery 48V	\N	Cells out of balance	\N	0.00	0.00	0	Received	Unpaid	\N	1	\N	2026-11-03 11:43:45.785742+00	\N	\N	\N	0.00
4	REP-20261103114346-1	\N	Tran Quoc Test	0933445566	Makita 18V Battery Pack	\N	Cells damaged, unbalanced voltage	Replaced bad cell and rebalanced BMS	0.00	120000.00	0	Completed	Unpaid	\N	1	\N	2026-11-03 11:43:46.219224+00	2026-11-03 11:43:46.299671+00	\N	\N	0.00
5	REP-20261103114804-1	\N	Nguyen Van Test	0911223344	E-Bike Battery 48V	\N	Cells out of balance	\N	0.00	0.00	0	Received	Unpaid	\N	1	\N	2026-11-03 11:48:04.731383+00	\N	\N	\N	0.00
7	REP-20261103114805-1	\N	Tran Quoc Test	0933445566	Makita 18V Battery Pack	\N	Cells damaged, unbalanced voltage	Replaced bad cell and rebalanced BMS	0.00	120000.00	0	Completed	Unpaid	\N	1	\N	2026-11-03 11:48:05.186012+00	2026-11-03 11:48:05.2591+00	\N	\N	0.00
8	REP-20261103114946-1	\N	Nguyen Van Test	0911223344	E-Bike Battery 48V	\N	Cells out of balance	\N	0.00	0.00	0	Received	Unpaid	\N	1	\N	2026-11-03 11:49:46.480151+00	\N	\N	\N	0.00
11	REP-20261103115109-1	\N	Nguyen Van Test	0911223344	E-Bike Battery 48V	\N	Cells out of balance	\N	0.00	0.00	0	Received	Unpaid	\N	1	\N	2026-11-03 11:51:09.815007+00	\N	\N	\N	0.00
13	REP-20261103115110-1	\N	Tran Quoc Test	0933445566	Makita 18V Battery Pack	\N	Cells damaged, unbalanced voltage	Replaced bad cell and rebalanced BMS	0.00	120000.00	0	Completed	Unpaid	\N	1	\N	2026-11-03 11:51:10.271376+00	2026-11-03 11:51:10.345044+00	\N	\N	0.00
14	REP-20261103115141-1	\N	Nguyen Van Test	0911223344	E-Bike Battery 48V	\N	Cells out of balance	\N	0.00	0.00	0	Received	Unpaid	\N	1	\N	2026-11-03 11:51:41.885882+00	\N	\N	\N	0.00
16	REP-20261103115142-1	\N	Tran Quoc Test	0933445566	Makita 18V Battery Pack	\N	Cells damaged, unbalanced voltage	Replaced bad cell and rebalanced BMS	0.00	120000.00	0	Completed	Unpaid	\N	1	\N	2026-11-03 11:51:42.213109+00	2026-11-03 11:51:42.24376+00	\N	\N	0.00
\.


--
-- Data for Name: reviews; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.reviews (review_id, user_id, product_id, order_item_id, rating, comment, created_at, updated_at) FROM stdin;
1	6	9	1	5	Khối pin Makita đóng rất chắc chắn, cell Sony xả khỏe máy cưa chạy mượt mà!	2026-10-26 12:00:00+00	\N
\.


--
-- Data for Name: roles; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.roles (role_id, role_name, description, created_at) FROM stdin;
1	Admin	Toàn quyền quản trị hệ thống	2026-11-03 07:53:47.612002+00
2	StoreManager	Quản lý vận hành chi nhánh, kho & tài chính cửa hàng	2026-11-03 07:53:47.612002+00
3	WarehouseStaff	Thủ kho phụ trách xuất nhập & kiểm toán linh kiện	2026-11-03 07:53:47.612002+00
4	SalesStaff	Thu ngân phụ trách bán lẻ tại quầy POS	2026-11-03 07:53:47.612002+00
5	Technician	Kỹ thuật viên đóng pin theo yêu cầu & sửa chữa thiết bị	2026-11-03 07:53:47.612002+00
6	User	Khách hàng thành viên mua sắm trực tuyến	2026-11-03 07:53:47.612002+00
\.


--
-- Data for Name: sale_items; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.sale_items (sale_item_id, sale_id, product_variant_id, product_name, variant_name, sku, unit_price, cost_price, quantity, warranty_months) FROM stdin;
1	1	1	Cell Pin Sony Murata US18650-VTC6 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	2	6
2	1	6	Vỏ Pin Makita 18V 10 Cell Kèm Mạch Báo Pin	Vỏ Pin Makita 18V 10 Cell Chân M21	CASE-MAKITA-10C	95000.00	65000.00	1	3
3	2	1	Sony VTC6 18650 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	1	6
4	4	1	Sony VTC6 18650 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	1	6
5	6	1	Sony VTC6 18650 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	1	6
6	8	1	Sony VTC6 18650 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	1	6
7	10	1	Sony VTC6 18650 3000mAh 30A	Sony VTC6 18650 3000mAh 30A	CELL-VTC6	85000.00	62000.00	1	6
\.


--
-- Data for Name: sales; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.sales (sale_id, sale_code, sold_by_user_id, customer_id, customer_name, customer_phone, subtotal_amount, discount_amount, total_amount, payment_method, sale_status, note, sold_at) FROM stdin;
1	POS-202610-001	4	8	Nguyễn Thị Hoa	0908999888	265000.00	0.00	265000.00	Cash	Completed	Khách lẻ mua cell lẻ và vỏ pin tại quầy	2026-10-25 04:20:00+00
2	POS-20261103114345-1	1	\N	\N	\N	85000.00	0.00	85000.00	Cash	Completed	\N	2026-11-03 11:43:45.566322+00
4	POS-20261103114804-1	1	\N	\N	\N	85000.00	0.00	85000.00	Cash	Completed	\N	2026-11-03 11:48:04.494863+00
6	POS-20261103114946-1	1	\N	\N	\N	85000.00	0.00	85000.00	Cash	Completed	\N	2026-11-03 11:49:46.362685+00
8	POS-20261103115109-1	1	\N	\N	\N	85000.00	0.00	85000.00	Cash	Completed	\N	2026-11-03 11:51:09.5691+00
10	POS-20261103115141-1	1	\N	\N	\N	85000.00	0.00	85000.00	Cash	Completed	\N	2026-11-03 11:51:41.768075+00
\.


--
-- Data for Name: shipments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.shipments (shipment_id, order_id, shipping_method_id, tracking_code, shipment_status, shipped_at, delivered_at, created_at, carrier_name, tracking_number, shipping_fee, note) FROM stdin;
1	1	1	GHTK-HCM-992211	Delivered	2026-10-22 08:00:00+00	2026-10-24 08:30:00+00	2026-10-22 03:30:00+00	\N	GHTK-HCM-992211	0.00	\N
2	2	1	GHTK-HCM-993344	Pending	\N	\N	2026-10-28 07:00:00+00	\N	GHTK-HCM-993344	0.00	\N
3	2	1	\N	InTransit	2026-11-03 11:51:42.073451+00	\N	2026-11-03 11:51:42.073314+00	GHTK	GHTK-2677	0.00	\N
\.


--
-- Data for Name: shipping_methods; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.shipping_methods (shipping_method_id, method_name, carrier_name, base_fee, estimated_delivery_days, is_active, created_at) FROM stdin;
1	Giao Hàng Tiết Kiệm (GHTK)	GHTK	25000.00	2-3 ngày	t	2026-11-03 07:53:47.63861+00
2	Viettel Post Nhanh	ViettelPost	35000.00	1-2 ngày	t	2026-11-03 07:53:47.63861+00
3	Hỏa Tốc Grab/Ahamove	GrabExpress	45000.00	2-4 giờ	t	2026-11-03 07:53:47.63861+00
\.


--
-- Data for Name: shop_expenses; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.shop_expenses (expense_id, expense_code, expense_category, amount, paid_by_user_id, note, paid_at) FROM stdin;
1	EXP-202610-001	Mặt bằng	12000000.00	2	Tiền thuê mặt bằng xưởng pin tháng 10/2026	2026-10-05 02:00:00+00
2	EXP-202610-002	Tiện ích điện nước	1850000.00	2	Tiền điện máy lạnh và máy hàn điểm cao tần tháng 10/2026	2026-10-20 07:30:00+00
\.


--
-- Data for Name: store_settings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.store_settings (setting_id, setting_key, setting_value, description, updated_at) FROM stdin;
1	store_name	Battery Parts Store - Xưởng Pin Công Cụ & Xe Điện	Tên thương hiệu cửa hàng hiển thị trên hóa đơn và website	2026-11-03 07:53:47.667572+00
2	hotline	0901234567	Số điện thoại hotline hỗ trợ kỹ thuật và mua hàng	2026-11-03 07:53:47.667572+00
3	email	support@batterypartsstore.vn	Email tiếp nhận phản hồi và bảo hành	2026-11-03 07:53:47.667572+00
4	address	123 Đường Lê Hồng Phong, Phường 2, Quận 10, TP. Hồ Chí Minh	Địa chỉ xưởng sản xuất và bảo hành trực tiếp	2026-11-03 07:53:47.667572+00
5	bank_info	Ngân hàng Quân Đội (MB Bank) - STK: 0901234567 - Chủ TK: NGUYEN VAN TUAN	Thông tin chuyển khoản thanh toán đơn hàng	2026-11-03 07:53:47.667572+00
6	tax_code	0318998877	Mã số thuế doanh nghiệp	2026-11-03 07:53:47.667572+00
\.


--
-- Data for Name: supplier_payments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.supplier_payments (supplier_payment_id, payment_code, purchase_receipt_id, created_by_user_id, amount, payment_method, payment_date, note, created_at) FROM stdin;
1	SPAY-202610-001	1	2	53400000.00	BankTransfer	2026-10-10 07:00:00+00	Chuyển khoản thanh toán toàn bộ đơn cell pin Hải Phòng Energy	2026-11-03 07:53:47.644339+00
2	SPAY-202610-002	2	2	20000000.00	BankTransfer	2026-10-13 04:00:00+00	Tạm ứng đợt 1 tiền mạch BMS Daly	2026-11-03 07:53:47.644339+00
\.


--
-- Data for Name: suppliers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.suppliers (supplier_id, supplier_code, name, contact_name, phone, email, address, is_active, created_at, updated_at) FROM stdin;
1	S-HP-ENERGY	Công ty TNHH Năng Lượng Xanh Hải Phòng	Nguyễn Văn Hải	0911222333	hai@hpenergy.vn	Lô A3 KCN Đình Vũ, Hải Phòng	t	2026-11-03 07:53:47.639711+00	\N
2	S-DALY-VN	Đại lý Phân phối Mạch BMS Daly Việt Nam	Trần Văn Đạt	0922333444	dat@daly.vn	Số 45 Đường số 8, KDC Cityland, Gò Vấp, TP.HCM	t	2026-11-03 07:53:47.639711+00	\N
3	S-BINHTAN	Xưởng Phụ kiện Khuôn Vỏ Pin Bình Tân	Lê Văn Tân	0933444555	tan@khuonpin.com	88 Mã Lò, Bình Trị Đông A, Bình Tân, TP.HCM	t	2026-11-03 07:53:47.639711+00	\N
\.


--
-- Data for Name: user_roles; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.user_roles (user_id, role_id, assigned_at) FROM stdin;
1	1	2026-11-03 07:53:47.618907+00
2	2	2026-11-03 07:53:47.618907+00
3	3	2026-11-03 07:53:47.618907+00
4	4	2026-11-03 07:53:47.618907+00
5	5	2026-11-03 07:53:47.618907+00
6	6	2026-11-03 07:53:47.618907+00
7	6	2026-11-03 07:53:47.618907+00
500	6	2026-11-03 11:48:03.008766+00
501	6	2026-11-03 11:48:03.77396+00
502	6	2026-11-03 11:48:04.846273+00
503	6	2026-11-03 11:49:45.342013+00
504	6	2026-11-03 11:49:45.845571+00
505	6	2026-11-03 11:49:46.553864+00
506	6	2026-11-03 11:51:08.096378+00
507	6	2026-11-03 11:51:08.838624+00
508	6	2026-11-03 11:51:09.926566+00
509	6	2026-11-03 11:51:40.732113+00
510	6	2026-11-03 11:51:41.228877+00
511	6	2026-11-03 11:51:41.962531+00
466	6	2026-11-03 11:43:44.173961+00
467	6	2026-11-03 11:43:44.871305+00
468	6	2026-11-03 11:43:45.893495+00
\.


--
-- Data for Name: users; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.users (user_id, username, password_hash, email, phone, full_name, avatar_url, is_active, created_at, updated_at, refresh_token, refresh_token_expiry_time) FROM stdin;
500	testuser_115391	100000.tuJ0GjHQrLAeoybN8n2rHg==.t1Gm0kTO09C5B6nc8eP9FTofjqxzVUI6o43Ormufel8=	test_115391@batterytest.vn	099115391	E2E Test User	\N	t	2026-11-03 11:48:02.962107+00	\N	4opIWTTKhN8W0giwFWMgpHi4RlzaPbWuAy0jKAriWnoX1Qk7Azr6QQg+PnbwpoEW1UcBUq1/BU8ZW06EZIiVbw==	2026-11-10 11:48:02.962075+00
468	cust_journey_107402	100000.YCXookt8w3ni+g2d+nULxw==.zAsetab7rNoCADBXYCveqh4hgK0qVOVnsKIW3tsFPuU=	cust_journey_107402@shopbattery.vn	091107402	Nguyen Journey Customer	\N	t	2026-11-03 11:43:45.893355+00	2026-11-03 11:43:45.92214+00	UHcM3rqAR+hMuQW6GYgms4NiyJnJFNYVjt4zrib2dJjLp4cc3Z6T59ku5QlOlr4VsuHBG9aEh1ksev5nbUgKFA==;X06aRJL/1rm1tkaDOZH1+nj2h2nOyGKUriMu9Q1bbn7HRs148UHmwXD8cB6B9FOZS2mOj7hR5OwxV3ZsHKpUYw==	2026-11-10 11:43:45.92214+00
3	warehouse	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	warehouse@batterypartsstore.vn	0903456789	Thủ Kho Nguyễn Văn Hùng	\N	t	2026-11-03 07:53:47.613752+00	2026-11-03 11:51:42.122561+00	r6q+RvM3BrJDf0gQx+nSvHYORGpspEhXggewbhah03NhU5oEbrXOX6hkPz034O86CabIf6IaRtlflFUuRv0gag==;2pqjJ/OY4/Qh7bDXnK25G91qfp9jf4EASfjNz1UN54IxKdAVyIq1KCA904tueh46ic7MDm7KsLL4Qm+geNW63Q==;BWEsrU36lrz6B9s85SJ70z09cnJVVibS5hor5IzfitBZ41MKcPftfL6jy47T8UpXDEwzdpFYfqamXRJcJY0bGA==	2026-11-10 11:51:42.122561+00
506	testuser_110080	100000.pe3O+PUAN2PyOB9HaXEHhQ==.rRrzKH3bHEMtngY/IryvBMeFTUj+ZkYtRuvo2B6cLUI=	test_110080@batterytest.vn	099110080	E2E Test User	\N	t	2026-11-03 11:51:08.051486+00	\N	6M8luG34GvDn/TjVxYKTx9dpYMGZ7i42PnR/qpp4o8Lj1qAvUwpmnCCtzT0375rKIkzzVKPGBTipgOiCPvq+rA==	2026-11-10 11:51:08.051455+00
7	customer2	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	tran.binh@gmail.com	0907890123	Trần Bình	\N	t	2026-11-03 07:53:47.613752+00	\N	\N	\N
505	cust_journey_123142	100000.l+p/bYhbu3hfuitDapEVNg==.wQyyLWmzwZncKQqH2YA2kYw4q9H+ivKDWq4Gn2VBDbE=	cust_journey_123142@shopbattery.vn	091123142	Nguyen Journey Customer	\N	t	2026-11-03 11:49:46.553706+00	2026-11-03 11:49:46.581758+00	lylycqkqEG/GYNG33tJeq2a4dxiBT/1KqNd4FkU/iCra7Bvj/xSz0vyOCAuqYVPjKVOpyrTjFQPVfCTs34qJLw==;IRXD3LWzQpr++oi+xcVancQlXEDMp2PkEgbXBsB3n27TQLGTTN7RWppFFQNNPfQP/+BcFenSsgpSvLif9YpwmA==	2026-11-10 11:49:46.581757+00
6	customer1	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	nguyen.an@gmail.com	0906789012	Nguyễn Văn An	\N	t	2026-11-03 07:53:47.613752+00	2026-11-03 11:51:41.42475+00	90iiOhnHfXwir88cy/7ubjo48b4Uo1naAUBTUDNOXtChHvVAMpr0cvxgb0HLvkNO2v6fNwyGbMUljqz1s2esjg==;qaOkPTodSqOO3Am4sjWTH3UR5a7M8OSws2xlzsJy6B4kLzAGOdQmQ1ks+a9eRS8cF8BAb5Ivhe25lTkkxF2EIw==;XcfVw6WlaGeaKKVln74PFkhRo0cyyClmcMyffVEBMOdel98TjdRgMZx8avLE3napdP4mJUVZs8TqcPbbIzmFpg==	2026-11-10 11:51:41.424749+00
502	cust_journey_117082	100000.pgtLlvALM8fHeg8wkCNCBQ==.3xLsPeLZWlT5CQobUWmdZIYMWz+6cEr291jRyGfxbsQ=	cust_journey_117082@shopbattery.vn	091117082	Nguyen Journey Customer	\N	t	2026-11-03 11:48:04.846075+00	2026-11-03 11:48:04.876206+00	k0W8M+t+e03P+yFHQ7NYgEB0FoH3JS66FbsFtkYzCS8geXeytGo+0Hb/3Jd1m6PbwqfWh0c6Usn4S2gY0pfjcQ==;iW3ytYOO6oSyEisaSXBAswHUC4iYnt55m3JxIKK6spkGMv5APPLHwizOAOhLNJMTdG6wRq3LiHNFBVe228Id/A==	2026-11-10 11:48:04.876205+00
501	xss_user_28118	100000.PmMt36x5mCWB5NQxfM8TLA==.5qRUsAaos15bhA6nYVLhgAIt/6qyxc01FT0V92JCBck=	xss_user_28118@test.vn	09888589	<script>alert('XSS')</script>	\N	t	2026-11-03 11:48:03.773362+00	\N	GJeTrQICnpl0MPcE6vrdv7bT/NbYtb4/1lT5AZdVr6EJ/MA7FrAbCwfJYec7o1c7+eghqzkYWf+oKgAXCQ/vvA==	2026-11-10 11:48:03.773361+00
507	xss_user_22431	100000.zPKY+c/yWBPcppojkg1YcQ==.2lZQS+7Xg7oouIGxx6wVr8iVrgh6jb63+/JABvAY1OA=	xss_user_22431@test.vn	0988140	<script>alert('XSS')</script>	\N	t	2026-11-03 11:51:08.838032+00	\N	rCK3pNFuYpwnJybHHKECQ/LzrjHOaMaW+lweQa1DzjY5VtVT1PrYP2VGqcUxfYaEjd2/ycazUIVKSDt3SlTxHg==	2026-11-10 11:51:08.838031+00
503	testuser_118233	100000.b+oJzkJUUZif4dN3efxAUQ==.lcQ/eT7R1iVAzyaKHfnWx9y8FIhTZJpatfjfNYb8FzY=	test_118233@batterytest.vn	099118233	E2E Test User	\N	t	2026-11-03 11:49:45.341602+00	\N	5HUFYAzZCfk9IJR0DGvSjqGjQtI+7fcz6BhlXKDN33Gri73m2H8uOcGOSlk+KrgcGdwhI4vhGHNiRuHINuw5HA==	2026-11-10 11:49:45.3416+00
2	manager	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	manager@batterypartsstore.vn	0902345678	Quản Lý Lê Văn Minh	\N	t	2026-11-03 07:53:47.613752+00	2026-11-03 11:51:42.046058+00	/7Y/Vce3Qq/sbBDcNSPIDeKwDwswQEI8si3SnhmxJ1Q9Ma3PyfrmVhF6bVf62v7x5oRFvODzWLjMmZB1pNgeVg==;R5Ky5pa5jHtLp8kwR2aLM7Z80XnBQwkM0J0azg4KAF1Qajg1PHbAfrPhhPCnV8l6SDuFAclPqIMLaHOgTPJ1GQ==;lL1366Q5ZG5fhOc+GkUjgd1Bvt84dbLx853Mollp5H39nG3EMjz8EY3L8XwC+fKUIlMuuPXBw4a5luOd/JtqPA==	2026-11-10 11:51:42.046057+00
504	xss_user_2246	100000.6Ni5TWZccgs2Ov6yShWdmQ==.dDWn3ZxgR5TT/xZ0vbOGgi33KyYrp5sT3SBNztI5XDA=	xss_user_2246@test.vn	098832189	<script>alert('XSS')</script>	\N	t	2026-11-03 11:49:45.845408+00	\N	Q4nD1/RjoQd2fazgofk1BY6Rgl7dW1e1rZGWqcWIFKNlNi2oVvYOBzani4L39wHQeQ5uMFg8xsGSrjLmNzDtnw==	2026-11-10 11:49:45.845407+00
509	testuser_129346	100000.Y7kG59zM6zobkHvbom+2AQ==.6MRLe5LnjAZxUSoNrRsETX/vsomypfnsRH3uY2vPXTU=	test_129346@batterytest.vn	099129346	E2E Test User	\N	t	2026-11-03 11:51:40.731681+00	\N	RctQdmqFz9Y7oNqF4eFWBhiF+s+MBTdApe59LO2ANqZMH1eRxLqSviAGraJenF8V+cHqm0GiuFiqO63TAi9R8A==	2026-11-10 11:51:40.731679+00
4	sales	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	sales@batterypartsstore.vn	0904567890	Thu Ngân Hoàng Thị Mai	\N	t	2026-11-03 07:53:47.613752+00	2026-11-03 11:59:10.932988+00	0VHCYMmmTkeWz4GDBkBGnrj73M9XnhLLUcNOmnSEF4SiIQzg0WfUC/+jJH914P66Vj2gUPtMhlXFQwy27kVM4g==;Yx5GTlvAQES0MBt3o2w39FntHDBKmE26g8pcUS45rbW0kOuDWXraxzx6wV5XThvfQ4H4gIdd1bveemd7ZRWkgw==;AaZvzsn7B4yk646BijYbJaRRIKmfy9mFLXFQPBEFnBuIJyhqx8Upv745jMKl34c4DYJqMX6f7Eymdp6gRiqg4A==	2026-11-10 11:59:10.932988+00
508	cust_journey_124413	100000.O7QYx4t9wuhuuEft42rJQw==.XnKE/j7tBMoJSf3VT8exohkuDuiD/1ZIOSG+IpYWCt0=	cust_journey_124413@shopbattery.vn	091124413	Nguyen Journey Customer	\N	t	2026-11-03 11:51:09.926408+00	2026-11-03 11:51:09.955868+00	/uXHvq/vCdHBI4Fbqd+et485n0PBgVv2UILBl7g95TwQyN5CO8z+X2sT4+eSLVuYzj34oz3ngd7B7HGPZLWgKA==;d2oTjR3mdtRPUBz4bjTiwWct0KgR5Q/jZJNDBDRSo6hSWU4/RLZ0jLJWwdh5E7yYuXbc8BdNoUqM59dVtxTmyw==	2026-11-10 11:51:09.955867+00
5	tech	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	tech@batterypartsstore.vn	0905678901	Kỹ Thuật Viên Trần Quốc Đạt	\N	t	2026-11-03 07:53:47.613752+00	2026-11-03 11:51:42.187994+00	20lzsAm3ONMMPZhWfyURbf2E3a9q/e9KRkM25DFhmsnauLHLH3+kqCy+BxFk1dzOCPV5RrgpcgBeeH3Hd0EIOw==;58Kmpi5O3dMIHl82XOF0V8d2z8jm2prg5mPZ8W9glFm/gL8Dl8YO1wn74lwTy25E8tpjhH4H2AdYwhF6VYMFbA==;gMsIujBI5xaziq2Z9S3sv1ltEUX7oVxY7ITEpgUNGsocEWyTwVylcf9CPPDvjBAqemXQQPIWE8pboASNK5Jveg==	2026-11-10 11:51:42.187994+00
467	xss_user_18105	100000.Lnm5rZyWAfkuugY3HYVxZw==.m1pJn0wvlLJJXGarSP0aUIa+rde1uQ3P+elmksphleU=	xss_user_18105@test.vn	098814498	<script>alert('XSS')</script>	\N	t	2026-11-03 11:43:44.870752+00	\N	p7d+4z36CQVkHjsX9QKIwXPG3hTO/lN6ayBkNP7l+YteV2XaCQpZW4tVGm5yoLzoym7+F2jbGut9RXEnbRUyjg==	2026-11-10 11:43:44.870751+00
466	testuser_122241	100000.yZBCAcan6qAaRYou02BGuw==.RZIG3pX0qSCiJAQUw7HUZhyN8tcT3crCvO0YTCg+HGY=	test_122241@batterytest.vn	099122241	E2E Test User	\N	t	2026-11-03 11:43:44.133455+00	\N	qLbbiLiTsJ6DUy35evbNHunepK+PiniN/2jEQQpo5cR7kIZqoIJs3Q6VvOBorsD2te4NkYse9lh/jdKGwiuwlA==	2026-11-10 11:43:44.133428+00
1	admin	100000.QwtvIQecYUsSSEGlS9bACA==.+OV+lvJN00dv47hTt+F8XCLCHyRxadwgtrDguqt9axg=	admin@batterypartsstore.vn	0901234567	Chủ Shop Pin Tuấn Hùng	\N	t	2026-11-03 07:53:47.613752+00	2026-11-08 11:50:15.209088+00	QgC0VjBspgFpvrCzIdJ9KSgxaQ8Uw6VM59fz7lhRMSyGFBIeDrkO+ldExE2BNge28ut7Db1YegV3uWF4asBiVw==;lDkWdBWCEB8gJjGmwIdIT7TOqXE1158WggxaXytBJkFB6e6tNifMWn9Khv79I87LUYTmyBi/oPTWGYOeOXwIyQ==;D1iK0SXiJCMIj8OZ0x4fE7lB2KucCIsTgxwNuWNuB5d1M7jZQ2MafT4srgFrpY4cAhpZO3px+rPl9zM2J8ea8A==	2026-11-15 11:50:15.209087+00
510	xss_user_2516	100000.uEpVteWxGzYoKKm/A8nKFg==.vrZSqrXNwdkvhj7v53DsKHd8HIfd2+ZeykBGW+WWDUc=	xss_user_2516@test.vn	098813923	<script>alert('XSS')</script>	\N	t	2026-11-03 11:51:41.228691+00	\N	zVeg8LFCWu8zzICZNeU6035UiVrJn1CK8URj9s1R8wwOiPG2l4QhH8OROEdxzPpawRNDC4V9fWcsC7NIPQxf5Q==	2026-11-10 11:51:41.22869+00
511	cust_journey_119910	100000.Qo50R5Ya1efs74ekFuxWPA==.kzoLxR78iQBRjf140Zf0V652xFhIQG4q/9SEHzxZ9Mo=	cust_journey_119910@shopbattery.vn	091119910	Nguyen Journey Customer	\N	t	2026-11-03 11:51:41.96239+00	2026-11-03 11:51:41.990739+00	bT9H1S8ShkaI6BAD21rnwyxFX54Pe5/BFo73JKcEmAbmTj5HW1HUGTg+8WnCwVOcXmURIvIlVtI2xfcbmVCBlQ==;CiAcKSwEbwVlwG5MIoae1lLBVf/HSzrDEMC2wZ2zQErptmq7PPLuXy5sigKD7IElQN8ZSIc3HN9uIQVuhhzoNQ==	2026-11-10 11:51:41.990738+00
\.


--
-- Data for Name: vouchers; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.vouchers (voucher_id, voucher_code, description, discount_type, discount_value, min_order_amount, max_discount_amount, usage_limit, used_count, start_date, end_date, is_active, created_at, updated_at) FROM stdin;
1	PINMOI2026	Giảm 50.000đ cho đơn đóng pin từ 500.000đ	FixedAmount	50000.00	500000.00	\N	100	1	2025-12-31 17:00:00+00	2026-12-31 16:59:59+00	t	2026-11-03 07:53:47.637388+00	\N
2	GIAM10	Giảm 10% tối đa 100.000đ cho đơn từ 300.000đ	Percentage	10.00	300000.00	100000.00	200	0	2025-12-31 17:00:00+00	2026-12-31 16:59:59+00	t	2026-11-03 07:53:47.637388+00	\N
\.


--
-- Name: addresses_address_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.addresses_address_id_seq', 2, true);


--
-- Name: assembly_order_items_assembly_order_item_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.assembly_order_items_assembly_order_item_id_seq', 8, true);


--
-- Name: assembly_orders_assembly_order_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.assembly_orders_assembly_order_id_seq', 2, true);


--
-- Name: attribute_values_attribute_value_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.attribute_values_attribute_value_id_seq', 8, true);


--
-- Name: attributes_attribute_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.attributes_attribute_id_seq', 3, true);


--
-- Name: categories_category_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.categories_category_id_seq', 4, true);


--
-- Name: customers_customer_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.customers_customer_id_seq', 1540, true);


--
-- Name: inventory_transactions_inventory_transaction_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.inventory_transactions_inventory_transaction_id_seq', 53, true);


--
-- Name: order_items_order_item_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.order_items_order_item_id_seq', 3, true);


--
-- Name: orders_order_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.orders_order_id_seq', 5, true);


--
-- Name: payments_payment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.payments_payment_id_seq', 2, true);


--
-- Name: product_images_image_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.product_images_image_id_seq', 10, true);


--
-- Name: product_variants_product_variant_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.product_variants_product_variant_id_seq', 10, true);


--
-- Name: products_product_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.products_product_id_seq', 10, true);


--
-- Name: purchase_receipt_items_purchase_receipt_item_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.purchase_receipt_items_purchase_receipt_item_id_seq', 8, true);


--
-- Name: purchase_receipts_purchase_receipt_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.purchase_receipts_purchase_receipt_id_seq', 3, true);


--
-- Name: repair_ticket_items_repair_ticket_item_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.repair_ticket_items_repair_ticket_item_id_seq', 5, true);


--
-- Name: repair_tickets_repair_ticket_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.repair_tickets_repair_ticket_id_seq', 16, true);


--
-- Name: reviews_review_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.reviews_review_id_seq', 1, true);


--
-- Name: roles_role_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.roles_role_id_seq', 6, true);


--
-- Name: sale_items_sale_item_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.sale_items_sale_item_id_seq', 7, true);


--
-- Name: sales_sale_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.sales_sale_id_seq', 11, true);


--
-- Name: shipments_shipment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.shipments_shipment_id_seq', 3, true);


--
-- Name: shipping_methods_shipping_method_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.shipping_methods_shipping_method_id_seq', 3, true);


--
-- Name: shop_expenses_expense_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.shop_expenses_expense_id_seq', 2, true);


--
-- Name: store_settings_setting_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.store_settings_setting_id_seq', 6, true);


--
-- Name: supplier_payments_supplier_payment_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.supplier_payments_supplier_payment_id_seq', 2, true);


--
-- Name: suppliers_supplier_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.suppliers_supplier_id_seq', 3, true);


--
-- Name: users_user_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.users_user_id_seq', 1586, true);


--
-- Name: vouchers_voucher_id_seq; Type: SEQUENCE SET; Schema: public; Owner: -
--

SELECT pg_catalog.setval('public.vouchers_voucher_id_seq', 2, true);


--
-- Name: addresses addresses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.addresses
    ADD CONSTRAINT addresses_pkey PRIMARY KEY (address_id);


--
-- Name: assembly_order_items assembly_order_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_order_items
    ADD CONSTRAINT assembly_order_items_pkey PRIMARY KEY (assembly_order_item_id);


--
-- Name: assembly_orders assembly_orders_assembly_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_orders
    ADD CONSTRAINT assembly_orders_assembly_code_key UNIQUE (assembly_code);


--
-- Name: assembly_orders assembly_orders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_orders
    ADD CONSTRAINT assembly_orders_pkey PRIMARY KEY (assembly_order_id);


--
-- Name: attribute_values attribute_values_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attribute_values
    ADD CONSTRAINT attribute_values_pkey PRIMARY KEY (attribute_value_id);


--
-- Name: attributes attributes_attribute_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attributes
    ADD CONSTRAINT attributes_attribute_code_key UNIQUE (attribute_code);


--
-- Name: attributes attributes_attribute_name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attributes
    ADD CONSTRAINT attributes_attribute_name_key UNIQUE (attribute_name);


--
-- Name: attributes attributes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attributes
    ADD CONSTRAINT attributes_pkey PRIMARY KEY (attribute_id);


--
-- Name: categories categories_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_pkey PRIMARY KEY (category_id);


--
-- Name: categories categories_slug_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_slug_key UNIQUE (slug);


--
-- Name: customers customers_phone_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_phone_key UNIQUE (phone);


--
-- Name: customers customers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_pkey PRIMARY KEY (customer_id);


--
-- Name: inventory inventory_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT inventory_pkey PRIMARY KEY (product_variant_id);


--
-- Name: inventory_transactions inventory_transactions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_transactions
    ADD CONSTRAINT inventory_transactions_pkey PRIMARY KEY (inventory_transaction_id);


--
-- Name: order_items order_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_pkey PRIMARY KEY (order_item_id);


--
-- Name: orders orders_order_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_order_code_key UNIQUE (order_code);


--
-- Name: orders orders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_pkey PRIMARY KEY (order_id);


--
-- Name: payments payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (payment_id);


--
-- Name: product_images product_images_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_pkey PRIMARY KEY (image_id);


--
-- Name: product_variant_attributes product_variant_attributes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variant_attributes
    ADD CONSTRAINT product_variant_attributes_pkey PRIMARY KEY (product_variant_id, attribute_value_id);


--
-- Name: product_variant_bundles product_variant_bundles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variant_bundles
    ADD CONSTRAINT product_variant_bundles_pkey PRIMARY KEY (bundle_variant_id, component_variant_id);


--
-- Name: product_variants product_variants_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variants
    ADD CONSTRAINT product_variants_pkey PRIMARY KEY (product_variant_id);


--
-- Name: product_variants product_variants_sku_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variants
    ADD CONSTRAINT product_variants_sku_key UNIQUE (sku);


--
-- Name: products products_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_pkey PRIMARY KEY (product_id);


--
-- Name: products products_slug_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_slug_key UNIQUE (slug);


--
-- Name: purchase_receipt_items purchase_receipt_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_items
    ADD CONSTRAINT purchase_receipt_items_pkey PRIMARY KEY (purchase_receipt_item_id);


--
-- Name: purchase_receipts purchase_receipts_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipts
    ADD CONSTRAINT purchase_receipts_pkey PRIMARY KEY (purchase_receipt_id);


--
-- Name: purchase_receipts purchase_receipts_receipt_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipts
    ADD CONSTRAINT purchase_receipts_receipt_code_key UNIQUE (receipt_code);


--
-- Name: repair_ticket_items repair_ticket_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_ticket_items
    ADD CONSTRAINT repair_ticket_items_pkey PRIMARY KEY (repair_ticket_item_id);


--
-- Name: repair_tickets repair_tickets_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_tickets
    ADD CONSTRAINT repair_tickets_pkey PRIMARY KEY (repair_ticket_id);


--
-- Name: repair_tickets repair_tickets_ticket_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_tickets
    ADD CONSTRAINT repair_tickets_ticket_code_key UNIQUE (ticket_code);


--
-- Name: reviews reviews_order_item_id_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_order_item_id_key UNIQUE (order_item_id);


--
-- Name: reviews reviews_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_pkey PRIMARY KEY (review_id);


--
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (role_id);


--
-- Name: roles roles_role_name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_role_name_key UNIQUE (role_name);


--
-- Name: sale_items sale_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT sale_items_pkey PRIMARY KEY (sale_item_id);


--
-- Name: sales sales_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT sales_pkey PRIMARY KEY (sale_id);


--
-- Name: sales sales_sale_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT sales_sale_code_key UNIQUE (sale_code);


--
-- Name: shipments shipments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipments
    ADD CONSTRAINT shipments_pkey PRIMARY KEY (shipment_id);


--
-- Name: shipping_methods shipping_methods_method_name_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipping_methods
    ADD CONSTRAINT shipping_methods_method_name_key UNIQUE (method_name);


--
-- Name: shipping_methods shipping_methods_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipping_methods
    ADD CONSTRAINT shipping_methods_pkey PRIMARY KEY (shipping_method_id);


--
-- Name: shop_expenses shop_expenses_expense_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_expenses
    ADD CONSTRAINT shop_expenses_expense_code_key UNIQUE (expense_code);


--
-- Name: shop_expenses shop_expenses_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_expenses
    ADD CONSTRAINT shop_expenses_pkey PRIMARY KEY (expense_id);


--
-- Name: store_settings store_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.store_settings
    ADD CONSTRAINT store_settings_pkey PRIMARY KEY (setting_id);


--
-- Name: store_settings store_settings_setting_key_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.store_settings
    ADD CONSTRAINT store_settings_setting_key_key UNIQUE (setting_key);


--
-- Name: supplier_payments supplier_payments_payment_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_payments
    ADD CONSTRAINT supplier_payments_payment_code_key UNIQUE (payment_code);


--
-- Name: supplier_payments supplier_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_payments
    ADD CONSTRAINT supplier_payments_pkey PRIMARY KEY (supplier_payment_id);


--
-- Name: suppliers suppliers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suppliers
    ADD CONSTRAINT suppliers_pkey PRIMARY KEY (supplier_id);


--
-- Name: suppliers suppliers_supplier_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.suppliers
    ADD CONSTRAINT suppliers_supplier_code_key UNIQUE (supplier_code);


--
-- Name: attribute_values uq_attribute_values; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attribute_values
    ADD CONSTRAINT uq_attribute_values UNIQUE (attribute_id, value_name);


--
-- Name: user_roles user_roles_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_pkey PRIMARY KEY (user_id, role_id);


--
-- Name: users users_email_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_email_key UNIQUE (email);


--
-- Name: users users_phone_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_phone_key UNIQUE (phone);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (user_id);


--
-- Name: users users_username_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- Name: vouchers vouchers_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.vouchers
    ADD CONSTRAINT vouchers_pkey PRIMARY KEY (voucher_id);


--
-- Name: vouchers vouchers_voucher_code_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.vouchers
    ADD CONSTRAINT vouchers_voucher_code_key UNIQUE (voucher_code);


--
-- Name: idx_addresses_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_addresses_user_id ON public.addresses USING btree (user_id);


--
-- Name: idx_assembly_order_items_assembly_order_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_assembly_order_items_assembly_order_id ON public.assembly_order_items USING btree (assembly_order_id);


--
-- Name: idx_assembly_orders_bundle_variant_id; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_assembly_orders_bundle_variant_id ON public.assembly_orders USING btree (bundle_variant_id);


--
-- Name: idx_categories_parent; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_categories_parent ON public.categories USING btree (parent_category_id);


--
-- Name: idx_inventory_transactions_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inventory_transactions_created_at ON public.inventory_transactions USING btree (created_at);


--
-- Name: idx_inventory_transactions_ref; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inventory_transactions_ref ON public.inventory_transactions USING btree (reference_type, reference_id);


--
-- Name: idx_inventory_transactions_variant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_inventory_transactions_variant ON public.inventory_transactions USING btree (product_variant_id);


--
-- Name: idx_order_items_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_order_items_order ON public.order_items USING btree (order_id);


--
-- Name: idx_order_items_variant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_order_items_variant ON public.order_items USING btree (product_variant_id);


--
-- Name: idx_orders_placed_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_placed_at ON public.orders USING btree (placed_at);


--
-- Name: idx_orders_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_status ON public.orders USING btree (order_status);


--
-- Name: idx_orders_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_orders_user ON public.orders USING btree (user_id);


--
-- Name: idx_payments_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_payments_order ON public.payments USING btree (order_id);


--
-- Name: idx_product_images_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_images_product ON public.product_images USING btree (product_id);


--
-- Name: idx_product_variant_attributes_value_variant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_variant_attributes_value_variant ON public.product_variant_attributes USING btree (attribute_value_id, product_variant_id);


--
-- Name: idx_product_variant_bundles_component; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_variant_bundles_component ON public.product_variant_bundles USING btree (component_variant_id);


--
-- Name: idx_product_variants_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_product_variants_product ON public.product_variants USING btree (product_id);


--
-- Name: idx_products_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_products_category ON public.products USING btree (category_id);


--
-- Name: idx_purchase_receipt_items_receipt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_purchase_receipt_items_receipt ON public.purchase_receipt_items USING btree (purchase_receipt_id);


--
-- Name: idx_purchase_receipt_items_variant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_purchase_receipt_items_variant ON public.purchase_receipt_items USING btree (product_variant_id);


--
-- Name: idx_purchase_receipts_date; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_purchase_receipts_date ON public.purchase_receipts USING btree (receipt_date);


--
-- Name: idx_purchase_receipts_supplier; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_purchase_receipts_supplier ON public.purchase_receipts USING btree (supplier_id);


--
-- Name: idx_repair_ticket_items_ticket; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_repair_ticket_items_ticket ON public.repair_ticket_items USING btree (repair_ticket_id);


--
-- Name: idx_repair_ticket_items_variant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_repair_ticket_items_variant ON public.repair_ticket_items USING btree (product_variant_id);


--
-- Name: idx_repair_tickets_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_repair_tickets_customer ON public.repair_tickets USING btree (customer_id);


--
-- Name: idx_repair_tickets_received_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_repair_tickets_received_at ON public.repair_tickets USING btree (received_at);


--
-- Name: idx_repair_tickets_status; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_repair_tickets_status ON public.repair_tickets USING btree (ticket_status);


--
-- Name: idx_repair_tickets_tech; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_repair_tickets_tech ON public.repair_tickets USING btree (technician_user_id);


--
-- Name: idx_reviews_product; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_reviews_product ON public.reviews USING btree (product_id);


--
-- Name: idx_reviews_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_reviews_user ON public.reviews USING btree (user_id);


--
-- Name: idx_sale_items_sale; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sale_items_sale ON public.sale_items USING btree (sale_id);


--
-- Name: idx_sale_items_variant; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sale_items_variant ON public.sale_items USING btree (product_variant_id);


--
-- Name: idx_sales_customer; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sales_customer ON public.sales USING btree (customer_id);


--
-- Name: idx_sales_sold_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sales_sold_at ON public.sales USING btree (sold_at);


--
-- Name: idx_sales_user; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_sales_user ON public.sales USING btree (sold_by_user_id);


--
-- Name: idx_shipments_order; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_shipments_order ON public.shipments USING btree (order_id);


--
-- Name: idx_shipments_tracking; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_shipments_tracking ON public.shipments USING btree (tracking_code);


--
-- Name: idx_shop_expenses_category; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_shop_expenses_category ON public.shop_expenses USING btree (expense_category);


--
-- Name: idx_shop_expenses_paid_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_shop_expenses_paid_at ON public.shop_expenses USING btree (paid_at);


--
-- Name: idx_supplier_payments_receipt; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX idx_supplier_payments_receipt ON public.supplier_payments USING btree (purchase_receipt_id);


--
-- Name: ux_customers_user_id; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_customers_user_id ON public.customers USING btree (user_id) WHERE (user_id IS NOT NULL);


--
-- Name: purchase_receipts trg_purchase_receipts_prevent_cancel_with_payments; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_purchase_receipts_prevent_cancel_with_payments BEFORE UPDATE OF receipt_status ON public.purchase_receipts FOR EACH ROW EXECUTE FUNCTION public.fn_trg_purchase_receipts_prevent_cancel_with_payments();


--
-- Name: supplier_payments trg_supplier_payments_sync_receipt_debt; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_supplier_payments_sync_receipt_debt AFTER INSERT OR DELETE OR UPDATE ON public.supplier_payments FOR EACH ROW EXECUTE FUNCTION public.fn_trg_supplier_payments_sync_receipt_debt();


--
-- Name: users trg_users_link_customer_on_register; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER trg_users_link_customer_on_register AFTER INSERT OR UPDATE OF phone ON public.users FOR EACH ROW EXECUTE FUNCTION public.fn_trg_users_link_customer_on_register();


--
-- Name: addresses addresses_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.addresses
    ADD CONSTRAINT addresses_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(user_id) ON DELETE CASCADE;


--
-- Name: assembly_order_items assembly_order_items_assembly_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_order_items
    ADD CONSTRAINT assembly_order_items_assembly_order_id_fkey FOREIGN KEY (assembly_order_id) REFERENCES public.assembly_orders(assembly_order_id);


--
-- Name: assembly_order_items assembly_order_items_component_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_order_items
    ADD CONSTRAINT assembly_order_items_component_variant_id_fkey FOREIGN KEY (component_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: assembly_orders assembly_orders_bundle_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_orders
    ADD CONSTRAINT assembly_orders_bundle_variant_id_fkey FOREIGN KEY (bundle_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: assembly_orders assembly_orders_created_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.assembly_orders
    ADD CONSTRAINT assembly_orders_created_by_user_id_fkey FOREIGN KEY (created_by_user_id) REFERENCES public.users(user_id);


--
-- Name: attribute_values attribute_values_attribute_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.attribute_values
    ADD CONSTRAINT attribute_values_attribute_id_fkey FOREIGN KEY (attribute_id) REFERENCES public.attributes(attribute_id) ON DELETE CASCADE;


--
-- Name: categories categories_parent_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categories
    ADD CONSTRAINT categories_parent_category_id_fkey FOREIGN KEY (parent_category_id) REFERENCES public.categories(category_id);


--
-- Name: customers customers_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.customers
    ADD CONSTRAINT customers_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(user_id);


--
-- Name: inventory inventory_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory
    ADD CONSTRAINT inventory_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: inventory_transactions inventory_transactions_created_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_transactions
    ADD CONSTRAINT inventory_transactions_created_by_user_id_fkey FOREIGN KEY (created_by_user_id) REFERENCES public.users(user_id);


--
-- Name: inventory_transactions inventory_transactions_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventory_transactions
    ADD CONSTRAINT inventory_transactions_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: order_items order_items_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(order_id) ON DELETE CASCADE;


--
-- Name: order_items order_items_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.order_items
    ADD CONSTRAINT order_items_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: orders orders_confirmed_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_confirmed_by_user_id_fkey FOREIGN KEY (confirmed_by_user_id) REFERENCES public.users(user_id);


--
-- Name: orders orders_shipping_method_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_shipping_method_id_fkey FOREIGN KEY (shipping_method_id) REFERENCES public.shipping_methods(shipping_method_id);


--
-- Name: orders orders_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(user_id);


--
-- Name: orders orders_voucher_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.orders
    ADD CONSTRAINT orders_voucher_id_fkey FOREIGN KEY (voucher_id) REFERENCES public.vouchers(voucher_id);


--
-- Name: payments payments_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(order_id);


--
-- Name: product_images product_images_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_images
    ADD CONSTRAINT product_images_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(product_id) ON DELETE CASCADE;


--
-- Name: product_variant_attributes product_variant_attributes_attribute_value_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variant_attributes
    ADD CONSTRAINT product_variant_attributes_attribute_value_id_fkey FOREIGN KEY (attribute_value_id) REFERENCES public.attribute_values(attribute_value_id) ON DELETE CASCADE;


--
-- Name: product_variant_attributes product_variant_attributes_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variant_attributes
    ADD CONSTRAINT product_variant_attributes_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id) ON DELETE CASCADE;


--
-- Name: product_variant_bundles product_variant_bundles_bundle_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variant_bundles
    ADD CONSTRAINT product_variant_bundles_bundle_variant_id_fkey FOREIGN KEY (bundle_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: product_variant_bundles product_variant_bundles_component_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variant_bundles
    ADD CONSTRAINT product_variant_bundles_component_variant_id_fkey FOREIGN KEY (component_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: product_variants product_variants_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.product_variants
    ADD CONSTRAINT product_variants_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(product_id) ON DELETE CASCADE;


--
-- Name: products products_category_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.products
    ADD CONSTRAINT products_category_id_fkey FOREIGN KEY (category_id) REFERENCES public.categories(category_id);


--
-- Name: purchase_receipt_items purchase_receipt_items_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_items
    ADD CONSTRAINT purchase_receipt_items_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: purchase_receipt_items purchase_receipt_items_purchase_receipt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipt_items
    ADD CONSTRAINT purchase_receipt_items_purchase_receipt_id_fkey FOREIGN KEY (purchase_receipt_id) REFERENCES public.purchase_receipts(purchase_receipt_id) ON DELETE CASCADE;


--
-- Name: purchase_receipts purchase_receipts_received_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipts
    ADD CONSTRAINT purchase_receipts_received_by_user_id_fkey FOREIGN KEY (received_by_user_id) REFERENCES public.users(user_id);


--
-- Name: purchase_receipts purchase_receipts_supplier_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.purchase_receipts
    ADD CONSTRAINT purchase_receipts_supplier_id_fkey FOREIGN KEY (supplier_id) REFERENCES public.suppliers(supplier_id);


--
-- Name: repair_ticket_items repair_ticket_items_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_ticket_items
    ADD CONSTRAINT repair_ticket_items_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: repair_ticket_items repair_ticket_items_repair_ticket_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_ticket_items
    ADD CONSTRAINT repair_ticket_items_repair_ticket_id_fkey FOREIGN KEY (repair_ticket_id) REFERENCES public.repair_tickets(repair_ticket_id) ON DELETE CASCADE;


--
-- Name: repair_tickets repair_tickets_created_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_tickets
    ADD CONSTRAINT repair_tickets_created_by_user_id_fkey FOREIGN KEY (created_by_user_id) REFERENCES public.users(user_id);


--
-- Name: repair_tickets repair_tickets_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_tickets
    ADD CONSTRAINT repair_tickets_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(customer_id);


--
-- Name: repair_tickets repair_tickets_technician_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.repair_tickets
    ADD CONSTRAINT repair_tickets_technician_user_id_fkey FOREIGN KEY (technician_user_id) REFERENCES public.users(user_id);


--
-- Name: reviews reviews_order_item_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_order_item_id_fkey FOREIGN KEY (order_item_id) REFERENCES public.order_items(order_item_id);


--
-- Name: reviews reviews_product_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_product_id_fkey FOREIGN KEY (product_id) REFERENCES public.products(product_id);


--
-- Name: reviews reviews_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reviews
    ADD CONSTRAINT reviews_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(user_id);


--
-- Name: sale_items sale_items_product_variant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT sale_items_product_variant_id_fkey FOREIGN KEY (product_variant_id) REFERENCES public.product_variants(product_variant_id);


--
-- Name: sale_items sale_items_sale_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sale_items
    ADD CONSTRAINT sale_items_sale_id_fkey FOREIGN KEY (sale_id) REFERENCES public.sales(sale_id) ON DELETE CASCADE;


--
-- Name: sales sales_customer_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT sales_customer_id_fkey FOREIGN KEY (customer_id) REFERENCES public.customers(customer_id);


--
-- Name: sales sales_sold_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.sales
    ADD CONSTRAINT sales_sold_by_user_id_fkey FOREIGN KEY (sold_by_user_id) REFERENCES public.users(user_id);


--
-- Name: shipments shipments_order_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipments
    ADD CONSTRAINT shipments_order_id_fkey FOREIGN KEY (order_id) REFERENCES public.orders(order_id);


--
-- Name: shipments shipments_shipping_method_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shipments
    ADD CONSTRAINT shipments_shipping_method_id_fkey FOREIGN KEY (shipping_method_id) REFERENCES public.shipping_methods(shipping_method_id);


--
-- Name: shop_expenses shop_expenses_paid_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.shop_expenses
    ADD CONSTRAINT shop_expenses_paid_by_user_id_fkey FOREIGN KEY (paid_by_user_id) REFERENCES public.users(user_id);


--
-- Name: supplier_payments supplier_payments_created_by_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_payments
    ADD CONSTRAINT supplier_payments_created_by_user_id_fkey FOREIGN KEY (created_by_user_id) REFERENCES public.users(user_id);


--
-- Name: supplier_payments supplier_payments_purchase_receipt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.supplier_payments
    ADD CONSTRAINT supplier_payments_purchase_receipt_id_fkey FOREIGN KEY (purchase_receipt_id) REFERENCES public.purchase_receipts(purchase_receipt_id);


--
-- Name: user_roles user_roles_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(role_id) ON DELETE CASCADE;


--
-- Name: user_roles user_roles_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.user_roles
    ADD CONSTRAINT user_roles_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(user_id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--


